using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Casos;

/// <summary>
/// Ejecuta lo que las reglas decidieron. La separacion es deliberada: las reglas devuelven
/// <see cref="AccionRegla"/> y esta clase es la unica que produce efectos, lo que permite probar
/// el comportamiento del sistema sin base de datos ni proveedor.
/// </summary>
public sealed class EjecutorAcciones(
    IWhatsAppProvider proveedor,
    IConversacionService conversaciones,
    IMensajeService mensajes,
    IPlantillaService plantillas,
    IJobFormsInvitacionService invitaciones,
    IPostulacionService postulaciones,
    IEventoSistemaService eventos,
    IAuditoriaService auditoria,
    ICuentaService cuentas,
    ILogger<EjecutorAcciones> log)
{
    /// <summary>Tope de opciones que WhatsApp admite en una lista interactiva.</summary>
    private const int MaximoOpcionesMenu = 10;

    /// <summary>Con mas de tres opciones WhatsApp exige lista en vez de botones.</summary>
    private const int MaximoBotones = 3;

    public async Task EjecutarAsync(
        IReadOnlyList<AccionRegla> acciones, ContextoRegla contexto, CancellationToken ct = default)
    {
        var conversacion = contexto.Conversacion
            ?? throw new InvalidOperationException("No se pueden ejecutar acciones sin conversacion.");

        foreach (var accion in acciones)
        {
            ct.ThrowIfCancellationRequested();

            // Un bloqueo corta el resto: es la unica accion que detiene el pipeline, porque lo que
            // sigue podria producir justamente el envio que se acaba de prohibir.
            if (accion is BloquearEnvio bloqueo)
            {
                log.LogWarning("Envio bloqueado en la conversacion {ConversacionId}: {Motivo}",
                    conversacion.ConversacionId, bloqueo.Motivo);
                return;
            }

            await EjecutarUnaAsync(accion, conversacion, contexto, ct);
        }
    }

    private async Task EjecutarUnaAsync(
        AccionRegla accion, Conversacion conversacion, ContextoRegla contexto, CancellationToken ct)
    {
        var id = conversacion.ConversacionId;

        switch (accion)
        {
            case EnviarPlantilla a:
                await EnviarPlantillaAsync(a, conversacion, contexto, ct);
                break;

            case EnviarTextoLibre a:
                await EnviarTextoLibreAsync(a.Texto, conversacion, contexto, ct);
                break;

            case MostrarMenuEmpresas a:
                await MostrarMenuEmpresasAsync(a, conversacion, contexto, ct);
                break;

            case MostrarMenuVacantes a:
                await MostrarMenuVacantesAsync(a, conversacion, contexto, ct);
                break;

            case EnviarLinkJobForms a:
                await EnviarLinkJobFormsAsync(a, conversacion, contexto, ct);
                break;

            case AsignarAnalista a:
                await conversaciones.AsignarAnalistaAsync(id, a.AnalistaId, a.Motivo, ct);
                break;

            case EscalarARespaldo a:
                await conversaciones.EscalarAsync(id, a.AnalistaRespaldoId, a.Motivo, ct);
                break;

            case EstablecerCuentaContexto a:
                await conversaciones.EstablecerCuentaContextoAsync(id, a.CuentaId, ct);
                break;

            case LimpiarCuentaContexto a:
                await conversaciones.LimpiarCuentaContextoAsync(id, a.Motivo, ct);
                break;

            case MarcarRecordatorioJobForms a:
                await invitaciones.MarcarRecordatorioEnviadoAsync(a.InvitacionId, ct);
                break;

            case MarcarAvisoAnalistaJobForms a:
                await invitaciones.MarcarAvisoAnalistaEnviadoAsync(a.InvitacionId, ct);
                break;

            case CambiarEstadoConversacion a:
                await conversaciones.CambiarEstadoAsync(id, a.Estado, ct);
                break;

            case RegistrarOptIn a:
                await conversaciones.RegistrarOptInAsync(id, a.Origen, ct);
                break;

            case ArchivarConversacion a:
                await conversaciones.CambiarEstadoAsync(id, EstadoConversacion.Archivada, ct);
                await auditoria.RegistrarAsync(
                    nameof(Conversacion), id.ToString(), null, "Archivado", a.Motivo, ct);
                break;

            case RegistrarAuditoria a:
                await auditoria.RegistrarAsync(
                    nameof(Conversacion), id.ToString(),
                    conversacion.AnalistaAtendiendoId, a.Accion, a.Detalle, ct);
                break;

            case NotificarAnalista a:
                // La bandeja lo recoge por SignalR; el evento es lo que sobrevive si el analista
                // no esta conectado en ese momento.
                await eventos.PublicarAsync(TiposEvento.AnalistaNotificado,
                    new { a.AnalistaId, a.Mensaje, ConversacionId = id }, contexto.CorrelationId, ct);
                break;

            case MoverEtapaKanban a:
                await postulaciones.MoverEtapaKanbanAsync(
                    a.PostulacionId, a.EtapaId, conversacion.AnalistaAtendiendoId ?? 0, ct);
                break;

            case PublicarEvento a:
                await eventos.PublicarAsync(a.Tipo, a.Payload, contexto.CorrelationId, ct);
                break;

            default:
                log.LogError("No hay ejecucion definida para la accion {Accion}.", accion.GetType().Name);
                break;
        }
    }

    private async Task EnviarPlantillaAsync(
        EnviarPlantilla accion, Conversacion conversacion, ContextoRegla contexto, CancellationToken ct)
    {
        if (!TieneOptIn(conversacion))
            return;

        // Devuelve nulo si no existe o si sigue inactiva por falta de aprobacion en Meta.
        var plantilla = await plantillas.ObtenerPlantillaParaEventoAsync(accion.ClavePlantilla, ct);

        if (plantilla is null)
        {
            await eventos.PublicarAsync(TiposEvento.EnvioOmitidoSinPlantilla,
                new { ConversacionId = conversacion.ConversacionId, accion.ClavePlantilla },
                contexto.CorrelationId, ct);

            return;
        }

        var resultado = await proveedor.EnviarPlantillaAsync(
            conversacion.TelefonoE164, plantilla, accion.Parametros, ct);

        await RegistrarSalienteAsync(
            conversacion, plantilla.TextoAprobado, plantilla.PlantillaId, resultado, contexto, ct,
            accion.Parametros);
    }

    private async Task EnviarTextoLibreAsync(
        string texto, Conversacion conversacion, ContextoRegla contexto, CancellationToken ct)
    {
        if (!TieneOptIn(conversacion) || !await VentanaAbiertaAsync(conversacion, ct))
            return;

        var resultado = await proveedor.EnviarTextoAsync(conversacion.TelefonoE164, texto, ct);

        await RegistrarSalienteAsync(conversacion, texto, null, resultado, contexto, ct);
    }

    /// <summary>
    /// Regla 9: crea la invitacion y le manda el enlace al postulante. La invitacion nace aca y no
    /// en la regla porque es un efecto: es la fila que despues sostiene el recordatorio de 24h y
    /// el aviso al analista de 48h.
    /// </summary>
    private async Task EnviarLinkJobFormsAsync(
        EnviarLinkJobForms accion, Conversacion conversacion, ContextoRegla contexto, CancellationToken ct)
    {
        if (!TieneOptIn(conversacion))
            return;

        var vacante = contexto.VacantesAbiertas.FirstOrDefault(v => v.HcId == accion.HcId);

        if (vacante is null)
        {
            log.LogError(
                "Se pidio el enlace de la vacante {HcId}, que no esta entre las abiertas de la cuenta.",
                accion.HcId);

            return;
        }

        var invitacion = await invitaciones.CrearInvitacionAsync(
            conversacion.ConversacionId, vacante.HcId, ct);

        var enlace = EnlaceJobForms.Construir(vacante.UrlJobForms, invitacion.Token);

        if (enlace is null)
        {
            // Falta un dato de administracion, no hay un error de codigo que corregir: la vacante
            // esta abierta pero nadie le cargo el formulario. Se deja en la outbox para que se vea.
            log.LogError(
                "La vacante {HcId} ({Titulo}) esta abierta pero no tiene formulario configurado.",
                vacante.HcId, vacante.Titulo);

            await eventos.PublicarAsync(TiposEvento.VacanteSinFormulario,
                new { vacante.HcId, vacante.Titulo, ConversacionId = conversacion.ConversacionId },
                contexto.CorrelationId, ct);

            return;
        }

        var texto = $"Para postular a {vacante.Titulo} completa esta ficha: {enlace}";

        await EnviarTextoLibreAsync(texto, conversacion, contexto, ct);
    }

    private async Task MostrarMenuEmpresasAsync(
        MostrarMenuEmpresas accion, Conversacion conversacion, ContextoRegla contexto, CancellationToken ct)
    {
        var disponibles = await cuentas.ListarConVacantesAbiertasAsync(ct);

        var texto = accion.EsReintento
            ? "No reconocimos tu respuesta. Elige una opcion del menu para continuar."
            : "Hola, soy el asistente automatico de reclutamiento. Indicanos a que empresa corresponde tu interes.";

        var opciones = disponibles
            .Select(c => new BotonRespuesta(IdsBoton.ParaCuenta(c.CuentaId), c.Nombre))
            .ToList();

        await EnviarMenuAsync(conversacion, texto, "Ver empresas", opciones, contexto, ct);
    }

    private async Task MostrarMenuVacantesAsync(
        MostrarMenuVacantes accion, Conversacion conversacion, ContextoRegla contexto, CancellationToken ct)
    {
        var cuenta = await cuentas.ObtenerPorIdAsync(accion.CuentaId, ct);

        var texto = cuenta is null
            ? "Estas son las vacantes disponibles. Elige a cual quieres postular."
            : $"Estas son las vacantes abiertas de {cuenta.Nombre}. Elige a cual quieres postular.";

        var opciones = contexto.VacantesAbiertas
            .Select(v => new BotonRespuesta(IdsBoton.ParaVacante(v.HcId), v.Titulo))
            .ToList();

        await EnviarMenuAsync(conversacion, texto, "Ver vacantes", opciones, contexto, ct);
    }

    /// <summary>
    /// Manda un menu, con botones o con lista segun cuantas opciones haya. Lo comparten el menu de
    /// empresas y el de vacantes para que los dos respeten el mismo tope y avisen igual cuando no
    /// entran todas.
    /// </summary>
    private async Task EnviarMenuAsync(
        Conversacion conversacion,
        string texto,
        string textoBoton,
        IReadOnlyList<BotonRespuesta> opciones,
        ContextoRegla contexto,
        CancellationToken ct)
    {
        if (!TieneOptIn(conversacion))
            return;

        if (!await VentanaAbiertaAsync(conversacion, ct))
            return;

        if (opciones.Count == 0)
        {
            log.LogWarning("No hay opciones disponibles: no se puede armar el menu.");

            await eventos.PublicarAsync(TiposEvento.MenuSinOpciones,
                new { ConversacionId = conversacion.ConversacionId }, contexto.CorrelationId, ct);

            return;
        }

        // WhatsApp no admite mas de 10 filas en una lista. Con mas opciones el menu se trunca y
        // algunos postulantes no verian la suya: hay que resolverlo con un menu de dos niveles o
        // con enlaces que ya traigan la eleccion hecha desde el aviso.
        if (opciones.Count > MaximoOpcionesMenu)
        {
            log.LogError(
                "Hay {Total} opciones y el menu solo admite {Maximo}. Se trunca.",
                opciones.Count, MaximoOpcionesMenu);

            await eventos.PublicarAsync(TiposEvento.MenuTruncado,
                new { Total = opciones.Count, Mostradas = MaximoOpcionesMenu },
                contexto.CorrelationId, ct);
        }

        var mostradas = opciones.Take(MaximoOpcionesMenu).ToList();

        var resultado = mostradas.Count <= MaximoBotones
            ? await proveedor.EnviarBotonesAsync(conversacion.TelefonoE164, texto, mostradas, ct)
            : await proveedor.EnviarListaAsync(conversacion.TelefonoE164, texto, textoBoton, mostradas, ct);

        await RegistrarSalienteAsync(conversacion, texto, null, resultado, contexto, ct);
    }

    /// <summary>
    /// Regla 15: sin opt-in no sale nada, ni siquiera con plantilla. Las reglas ya lo validan, pero
    /// esta clase es la que efectivamente llama al proveedor, asi que tambien lo comprueba.
    /// </summary>
    private bool TieneOptIn(Conversacion conversacion)
    {
        if (conversacion.FechaOptIn is not null)
            return true;

        log.LogWarning(
            "Se omitio un envio a la conversacion {ConversacionId}: no hay opt-in registrado.",
            conversacion.ConversacionId);

        return false;
    }

    /// <summary>
    /// Segunda barrera de la Regla 15. La regla ya valido, pero el texto libre fuera de la ventana
    /// es exactamente el envio que Meta sanciona: no se depende de una sola guarda.
    /// </summary>
    private async Task<bool> VentanaAbiertaAsync(Conversacion conversacion, CancellationToken ct)
    {
        if (await plantillas.ValidarVentana24hAsync(conversacion.ConversacionId, ct))
            return true;

        log.LogWarning(
            "Se intento texto libre fuera de la ventana de 24h en la conversacion {ConversacionId}. Se omite.",
            conversacion.ConversacionId);

        return false;
    }

    private async Task RegistrarSalienteAsync(
        Conversacion conversacion, string contenido, int? plantillaId,
        ResultadoEnvio resultado, ContextoRegla contexto, CancellationToken ct,
        IReadOnlyList<string>? parametrosPlantilla = null)
    {
        // Se registra tambien lo que fallo: el hilo de la bandeja tiene que reflejar lo que se
        // intento, y el error del proveedor es lo que permite diagnosticar despues.
        var mensaje = await mensajes.RegistrarSalienteAsync(
            conversacion.ConversacionId, contenido, plantillaId,
            analistaId: null, resultado.ProviderMessageId, contexto.CorrelationId,
            parametrosPlantilla, ct);

        if (resultado.Exito)
            return;

        log.LogError("Fallo el envio en la conversacion {ConversacionId} ({Clase}): {Error}",
            conversacion.ConversacionId, resultado.Clase, resultado.Error);

        // Solo lo transitorio se agenda para otro intento; lo permanente no cambia reintentando y
        // lo ambiguo pudo haber salido, asi que reintentarlo podria duplicarlo.
        var proximoIntento = resultado.Clase == ClaseFallo.Transitorio
            ? contexto.AhoraUtc.AddSeconds(SegundosPrimerReintento(contexto))
            : (DateTime?)null;

        // Por MensajeId y no por el id del proveedor: un envio rechazado no devuelve ninguno,
        // de modo que buscarlo por ahi no encontraria la fila y el fallo quedaria solo en el
        // log, con el mensaje eternamente en Pendiente y sin motivo (Seccion 9.6.2).
        await mensajes.MarcarEnvioFallidoAsync(
            mensaje.MensajeId, resultado.Error, resultado.Clase, proximoIntento, ct);
    }

    private static int SegundosPrimerReintento(ContextoRegla contexto) =>
        contexto.ConfigInt(ClavesConfiguracion.EnvioReintentoBaseSegundos, 60);
}
