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
/// <para>
/// No envia nada (V29): los mensajes que deciden las reglas se encolan con una clave unica y los
/// manda <see cref="DespachoEnvios"/>. Un envio no se puede deshacer, asi que mientras salia desde
/// aca, cualquier fallo posterior reprocesaba el evento y el postulante recibia todo otra vez (C5).
/// </para>
/// </summary>
public sealed class EjecutorAcciones(
    IConversacionService conversaciones,
    IMensajeService mensajes,
    IPlantillaService plantillas,
    IJobFormsInvitacionService invitaciones,
    IPostulacionService postulaciones,
    IEventoSistemaService eventos,
    IAuditoriaService auditoria,
    IAlertaOperativaService alertas,
    ICuentaService cuentas,
    IAnalistaService analistas,
    ILogger<EjecutorAcciones> log)
{
    /// <summary>Tope de opciones que WhatsApp admite en una lista interactiva.</summary>
    private const int MaximoOpcionesMenu = 10;

    /// <summary>Con mas de tres opciones WhatsApp exige lista en vez de botones.</summary>
    private const int MaximoBotones = 3;

    /// <summary>
    /// Cuentas por pagina cuando hay que paginar: la fila diez queda para navegar (FUN-03). Es un
    /// limite de Meta —diez filas por lista— y no una decision de negocio.
    /// </summary>
    private const int FilasPorPaginaConNavegacion = MaximoOpcionesMenu - 1;

    /// <summary>Largo maximo del titulo de una fila de lista en WhatsApp. Tambien es limite de Meta.</summary>
    private const int MaximoTituloOpcion = 24;

    /// <summary>
    /// Recorta el titulo al limite de Meta. Mandar uno mas largo hace que el mensaje sea rechazado
    /// entero: el postulante no veria el menu en vez de verlo con un nombre cortado.
    /// </summary>
    private static string Recortar(string titulo) =>
        titulo.Length <= MaximoTituloOpcion ? titulo : titulo[..(MaximoTituloOpcion - 1)] + "…";

    public async Task EjecutarAsync(
        IReadOnlyList<AccionRegla> acciones, ContextoRegla contexto, CancellationToken ct = default)
    {
        var conversacion = contexto.Conversacion
            ?? throw new InvalidOperationException("No se pueden ejecutar acciones sin conversacion.");

        // COR-03: si el envio anterior no se encolo —no hay ventana y la plantilla sigue sin aprobar—,
        // lo que iba a sellarlo se saltea. Sellar un recordatorio que no salio lo pierde para siempre.
        var ultimoEnvioEncolado = false;

        for (var indice = 0; indice < acciones.Count; indice++)
        {
            ct.ThrowIfCancellationRequested();

            var accion = acciones[indice];

            // Un bloqueo corta el resto: es la unica accion que detiene el pipeline, porque lo que
            // sigue podria producir justamente el envio que se acaba de prohibir.
            if (accion is BloquearEnvio bloqueo)
            {
                log.LogWarning("Envio bloqueado en la conversacion {ConversacionId}: {Motivo}",
                    conversacion.ConversacionId, bloqueo.Motivo);
                return;
            }

            if (DependeDelEnvioAnterior(accion) && !ultimoEnvioEncolado)
            {
                log.LogWarning(
                    "No se ejecuta {Accion} en la conversacion {ConversacionId}: el envio anterior no salio.",
                    accion.GetType().Name, conversacion.ConversacionId);

                continue;
            }

            // La posicion de la accion completa la clave del envio: el mismo disparador evaluado
            // otra vez decide lo mismo en el mismo orden y produce las mismas claves (V29).
            var clave = $"{contexto.ClaveEjecucion}:{indice}";

            // Nulo cuando la accion no era un envio: el resultado del ultimo envio sigue valiendo.
            if (await EjecutarUnaAsync(accion, conversacion, contexto, clave, ct) is { } encolado)
                ultimoEnvioEncolado = encolado;
        }
    }

    /// <summary>
    /// Ejecuta una accion. Devuelve si el envio quedo encolado, o nulo si la accion no era un envio:
    /// es lo que decide el sellado condicional de la accion siguiente (COR-03).
    /// </summary>
    private async Task<bool?> EjecutarUnaAsync(
        AccionRegla accion, Conversacion conversacion, ContextoRegla contexto, string clave, CancellationToken ct)
    {
        var id = conversacion.ConversacionId;

        switch (accion)
        {
            case EnviarPlantilla a:
                return await EnviarPlantillaAsync(a, conversacion, contexto, clave, ct);

            case EnviarTextoLibre a:
                return await EnviarTextoLibreAsync(a.Texto, conversacion, contexto, clave, ct);

            case EnviarMensajeBot a:
                return await EnviarMensajeBotAsync(a, conversacion, contexto, clave, ct);

            case MostrarMenuEmpresas a:
                return await MostrarMenuEmpresasAsync(a, conversacion, contexto, clave, ct);

            case MostrarMenuVacantes a:
                return await MostrarMenuVacantesAsync(a, conversacion, contexto, clave, ct);

            case MostrarMenuProcesos:
                return await MostrarMenuProcesosAsync(conversacion, contexto, clave, ct);

            case EnviarLinkJobForms a:
                return await EnviarLinkJobFormsAsync(a, conversacion, contexto, clave, ct);

            case AsignarAnalista a:
                await conversaciones.AsignarAnalistaAsync(id, a.AnalistaId, a.Motivo, ct);
                break;

            case EscalarARespaldo a:
                await conversaciones.EscalarAsync(id, a.AnalistaEsperadoId, a.AnalistaRespaldoId, a.Motivo, ct);
                break;

            case EstablecerCuentaContexto a:
                await conversaciones.EstablecerCuentaContextoAsync(id, a.CuentaId, ct);
                break;

            case LimpiarCuentaContexto a:
                await conversaciones.LimpiarCuentaContextoAsync(id, a.Motivo, ct);
                break;

            case TomarContextoDePostulacion a:
                await conversaciones.TomarContextoDePostulacionAsync(id, a.PostulacionId, ct);
                break;

            case RegistrarIntentoMenu a:
                await conversaciones.RegistrarIntentoMenuAsync(id, a.TextoNoReconocido, ct);
                break;

            case ReiniciarIntentosMenu:
                await conversaciones.ReiniciarIntentosMenuAsync(id, ct);
                break;

            case DerivarAPendientes a:
                await conversaciones.DerivarAPendientesAsync(id, a.Motivo, ct);
                break;

            case VencerTransferencia a:
                await conversaciones.VencerTransferenciaAsync(a.TransferenciaId, ct);
                break;

            case ReactivarConversacion a:
                await conversaciones.ReactivarAsync(id, a.Nuevo, ct);
                break;

            case SellarConversacion a:
                await conversaciones.SellarAsync(id, a.Marca, ct);
                break;

            case ArchivarPostulacion a:
                await postulaciones.ArchivarAsync(a.PostulacionId, a.Motivo, ct);
                break;

            case SellarPostulacion a:
                await postulaciones.SellarAsync(a.PostulacionId, a.Marca, ct);
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

            case NotificarRol a:
                await NotificarRolAsync(a, id, contexto, ct);
                break;

            case MoverEtapaKanban a:
                await postulaciones.MoverEtapaKanbanAsync(
                    a.PostulacionId, a.EtapaId, conversacion.AnalistaAtendiendoId ?? 0, ct);
                break;

            case PublicarEvento a:
                await eventos.PublicarAsync(a.Tipo, a.Payload, contexto.CorrelationId, ct);
                break;

            case RequierePlantilla:
                // Senal para quien arma una respuesta del analista (Regla 15); aca no hay nada que hacer.
                break;

            default:
                log.LogError("No hay ejecucion definida para la accion {Accion}.", accion.GetType().Name);
                break;
        }

        return null;
    }

    /// <summary>COR-03: acciones que solo tienen sentido si el envio inmediatamente anterior se encolo.</summary>
    private static bool DependeDelEnvioAnterior(AccionRegla accion) =>
        accion is SellarConversacion { SoloSiSeEnvioAnterior: true }
            or SellarPostulacion { SoloSiSeEnvioAnterior: true }
            or MarcarRecordatorioJobForms { SoloSiSeEnvioAnterior: true };

    /// <summary>
    /// FUN-05, FUN-06: un aviso por cada analista activo del rol. Si nadie lo tiene, queda en el log:
    /// es un problema de administracion —no hay Jefatura activa—, no una decision de la regla.
    /// </summary>
    private async Task NotificarRolAsync(
        NotificarRol accion, int conversacionId, ContextoRegla contexto, CancellationToken ct)
    {
        var destinatarios = await analistas.ListarActivosPorRolAsync(accion.Rol, ct);

        if (destinatarios.Count == 0)
        {
            log.LogError(
                "Nadie activo con el rol {Rol}: el aviso de la conversacion {ConversacionId} no llega a nadie.",
                accion.Rol, conversacionId);

            return;
        }

        foreach (var destinatario in destinatarios)
        {
            await eventos.PublicarAsync(TiposEvento.AnalistaNotificado,
                new { destinatario.AnalistaId, accion.Mensaje, ConversacionId = conversacionId },
                contexto.CorrelationId, ct);
        }
    }

    /// <summary>
    /// COR-03 (P1, C3): el bot habla en texto mientras la ventana de 24h esta abierta, y recien fuera
    /// de ella depende de una plantilla aprobada. Antes todo mensaje del bot era plantilla, y como las
    /// seis nacen inactivas, el postulante no recibia ni la confirmacion de su formulario.
    /// </summary>
    private async Task<bool> EnviarMensajeBotAsync(
        EnviarMensajeBot accion, Conversacion conversacion, ContextoRegla contexto, string clave, CancellationToken ct)
    {
        if (!TieneOptIn(conversacion))
            return false;

        if (await plantillas.ValidarVentana24hAsync(conversacion.ConversacionId, ct))
        {
            return await EncolarAsync(
                conversacion, new SalienteEncolado(TipoSaliente.Texto, accion.Texto), contexto, clave, ct);
        }

        if (accion.ClavePlantilla is not { } clavePlantilla)
        {
            log.LogWarning(
                "El mensaje del bot para la conversacion {ConversacionId} no tiene plantilla y la ventana esta cerrada.",
                conversacion.ConversacionId);

            return false;
        }

        // Devuelve nulo si no existe o si sigue inactiva por falta de aprobacion en Meta.
        var plantilla = await plantillas.ObtenerPlantillaParaEventoAsync(clavePlantilla, ct);

        if (plantilla is null)
        {
            // V32: alguien tiene que aprobarla en Meta. Una alerta por plantilla, no un evento por
            // postulante que nadie lee (M1). Lo que dependa de este envio no se sella.
            await alertas.RegistrarAsync(TiposAlerta.PlantillaNoAprobada, $"plantilla:{clavePlantilla}",
                $"El bot necesito la plantilla '{clavePlantilla}' fuera de la ventana y no esta activa.", ct);

            return false;
        }

        return await EncolarAsync(conversacion, new SalienteEncolado(
            TipoSaliente.Plantilla, plantilla.TextoAprobado, plantilla.PlantillaId, accion.Parametros),
            contexto, clave, ct);
    }

    /// <summary>
    /// FUN-09 (A3): los procesos vivos del postulante, para que diga por cual escribe en vez de que el
    /// bot lo adivine. La salida «Otra empresa» existe porque puede estar escribiendo por una nueva.
    /// </summary>
    private async Task<bool> MostrarMenuProcesosAsync(
        Conversacion conversacion, ContextoRegla contexto, string clave, CancellationToken ct)
    {
        var vivos = contexto.PostulacionesDelPostulante
            .Where(p => p.Estado is EstadoPostulacion.EnProceso or EstadoPostulacion.Reingreso)
            .ToList();

        // La cuenta primero, que es lo que el postulante reconoce; la vacante desempata cuando tiene
        // dos procesos vivos en la misma empresa (FUN-09).
        var opciones = vivos
            .Select(p => new BotonRespuesta(
                IdsBoton.ParaProceso(p.PostulacionId),
                Recortar($"{p.Cuenta}: {p.Vacante}")))
            .Append(new BotonRespuesta(IdsBoton.OtraEmpresa, "Otra empresa"))
            .ToList();

        return await EnviarMenuAsync(
            conversacion,
            "Tienes mas de un proceso con nosotros. Elige por cual nos escribes.",
            "Ver mis procesos",
            opciones,
            contexto,
            clave,
            ct);
    }

    private async Task<bool> EnviarPlantillaAsync(
        EnviarPlantilla accion, Conversacion conversacion, ContextoRegla contexto, string clave, CancellationToken ct)
    {
        if (!TieneOptIn(conversacion))
            return false;

        // Devuelve nulo si no existe o si sigue inactiva por falta de aprobacion en Meta.
        var plantilla = await plantillas.ObtenerPlantillaParaEventoAsync(accion.ClavePlantilla, ct);

        if (plantilla is null)
        {
            // V32: alguien tiene que aprobarla en Meta o activarla. Una alerta por plantilla, no un
            // evento por postulante que nadie lee (M1).
            await alertas.RegistrarAsync(TiposAlerta.PlantillaNoAprobada, $"plantilla:{accion.ClavePlantilla}",
                $"El bot necesito la plantilla '{accion.ClavePlantilla}' y no esta activa: el mensaje no salio.", ct);

            return false;
        }

        return await EncolarAsync(conversacion, new SalienteEncolado(
            TipoSaliente.Plantilla, plantilla.TextoAprobado, plantilla.PlantillaId, accion.Parametros),
            contexto, clave, ct);
    }

    private async Task<bool> EnviarTextoLibreAsync(
        string texto, Conversacion conversacion, ContextoRegla contexto, string clave, CancellationToken ct)
    {
        if (!TieneOptIn(conversacion) || !await VentanaAbiertaAsync(conversacion, ct))
            return false;

        return await EncolarAsync(conversacion, new SalienteEncolado(TipoSaliente.Texto, texto), contexto, clave, ct);
    }

    /// <summary>
    /// Regla 9: crea la invitacion y le manda el enlace al postulante. La invitacion nace aca y no
    /// en la regla porque es un efecto: es la fila que despues sostiene el recordatorio de 24h y
    /// el aviso al analista de 48h.
    /// </summary>
    private async Task<bool> EnviarLinkJobFormsAsync(
        EnviarLinkJobForms accion, Conversacion conversacion, ContextoRegla contexto, string clave, CancellationToken ct)
    {
        if (!TieneOptIn(conversacion))
            return false;

        var vacante = contexto.VacantesAbiertas.FirstOrDefault(v => v.HcId == accion.HcId);

        if (vacante is null)
        {
            log.LogError(
                "Se pidio el enlace de la vacante {HcId}, que no esta entre las abiertas de la cuenta.",
                accion.HcId);

            return false;
        }

        var invitacion = await invitaciones.CrearInvitacionAsync(
            conversacion.ConversacionId, vacante.HcId, ct);

        var enlace = EnlaceJobForms.Construir(vacante.UrlJobForms, invitacion.Token);

        if (enlace is null)
        {
            // Falta un dato de administracion, no hay un error de codigo que corregir: la vacante
            // esta abierta pero nadie le cargo el formulario. Queda como alerta para que se vea (V32).
            log.LogError(
                "La vacante {HcId} ({Titulo}) esta abierta pero no tiene formulario configurado.",
                vacante.HcId, vacante.Titulo);

            await alertas.RegistrarAsync(TiposAlerta.VacanteSinFormulario, $"hc:{vacante.HcId}",
                $"La vacante '{vacante.Titulo}' esta abierta sin formulario: los postulantes no reciben el enlace.", ct);

            return false;
        }

        var texto = $"Para postular a {vacante.Titulo} completa esta ficha: {enlace}";

        return await EnviarTextoLibreAsync(texto, conversacion, contexto, clave, ct);
    }

    private async Task<bool> MostrarMenuEmpresasAsync(
        MostrarMenuEmpresas accion, Conversacion conversacion, ContextoRegla contexto, string clave, CancellationToken ct)
    {
        var disponibles = await cuentas.ListarMenuAsync(ct);

        var texto = accion switch
        {
            { EsReintento: true } => "No reconocimos tu respuesta. Elige una opcion del menu para continuar.",
            { EsRegreso: true } => "Hola de nuevo. Para ayudarte mejor, indicanos a que empresa corresponde tu consulta.",
            _ => "Hola, soy el asistente automatico de reclutamiento. Indicanos a que empresa corresponde tu interes."
        };

        return await EnviarMenuAsync(
            conversacion, texto, "Ver empresas", Paginar(disponibles, accion.Pagina), contexto, clave, ct);
    }

    /// <summary>
    /// FUN-03: las cuentas que entran en la pagina pedida, con la fila de navegacion al final. Con 10 o
    /// menos no hace falta paginar y van todas.
    /// <para>
    /// Antes se cortaba en las primeras 10 y el resto no existia para el postulante: la cuenta 11 en
    /// adelante no se podia elegir por el menu (AL5).
    /// </para>
    /// </summary>
    private static List<BotonRespuesta> Paginar(IReadOnlyList<Cuenta> cuentas, int pagina)
    {
        var opciones = cuentas
            .Select(c => new BotonRespuesta(IdsBoton.ParaCuenta(c.CuentaId), c.Nombre))
            .ToList();

        if (opciones.Count <= MaximoOpcionesMenu)
            return opciones;

        var paginas = (int)Math.Ceiling((double)opciones.Count / FilasPorPaginaConNavegacion);
        var actual = Math.Clamp(pagina <= 0 ? 1 : pagina, 1, paginas);

        var mostradas = opciones
            .Skip((actual - 1) * FilasPorPaginaConNavegacion)
            .Take(FilasPorPaginaConNavegacion)
            .ToList();

        mostradas.Add(actual < paginas
            ? new BotonRespuesta(IdsBoton.ParaPagina(actual + 1), "Ver mas empresas")
            : new BotonRespuesta(IdsBoton.ParaPagina(1), "Volver al inicio"));

        return mostradas;
    }

    private async Task<bool> MostrarMenuVacantesAsync(
        MostrarMenuVacantes accion, Conversacion conversacion, ContextoRegla contexto, string clave, CancellationToken ct)
    {
        var cuenta = await cuentas.ObtenerPorIdAsync(accion.CuentaId, ct);

        var texto = cuenta is null
            ? "Estas son las vacantes disponibles. Elige a cual quieres postular."
            : $"Estas son las vacantes abiertas de {cuenta.Nombre}. Elige a cual quieres postular.";

        var opciones = contexto.VacantesAbiertas
            .Select(v => new BotonRespuesta(IdsBoton.ParaVacante(v.HcId), v.Titulo))
            .ToList();

        return await EnviarMenuAsync(conversacion, texto, "Ver vacantes", opciones, contexto, clave, ct);
    }

    /// <summary>
    /// Encola un menu, con botones o con lista segun cuantas opciones haya. Lo comparten el menu de
    /// empresas y el de vacantes para que los dos respeten el mismo tope y avisen igual cuando no
    /// entran todas.
    /// </summary>
    private async Task<bool> EnviarMenuAsync(
        Conversacion conversacion,
        string texto,
        string textoBoton,
        IReadOnlyList<BotonRespuesta> opciones,
        ContextoRegla contexto,
        string clave,
        CancellationToken ct)
    {
        if (!TieneOptIn(conversacion))
            return false;

        if (!await VentanaAbiertaAsync(conversacion, ct))
            return false;

        if (opciones.Count == 0)
        {
            log.LogWarning("No hay opciones disponibles: no se puede armar el menu.");

            await alertas.RegistrarAsync(TiposAlerta.MenuSinOpciones, "menu",
                "El bot no tiene opciones que ofrecer: no hay cuentas o vacantes abiertas.", ct);

            return false;
        }

        // WhatsApp no admite mas de 10 filas en una lista. Quien arma el menu ya lo respeto —el de
        // empresas paginando (FUN-03)—, asi que llegar aca con mas es un error de programacion.
        if (opciones.Count > MaximoOpcionesMenu)
        {
            throw new InvalidOperationException(
                $"Se intento un menu de {opciones.Count} opciones y WhatsApp admite {MaximoOpcionesMenu}.");
        }

        var mostradas = opciones;

        var saliente = mostradas.Count <= MaximoBotones
            ? new SalienteEncolado(TipoSaliente.Botones, texto, Opciones: mostradas)
            : new SalienteEncolado(TipoSaliente.Lista, texto, Opciones: mostradas, TextoBotonLista: textoBoton);

        return await EncolarAsync(conversacion, saliente, contexto, clave, ct);
    }

    /// <summary>
    /// Regla 15: sin opt-in no sale nada, ni siquiera con plantilla. Las reglas ya lo validan y el
    /// despachador lo revalida al enviar; esta es la guarda para no encolar lo que no puede salir.
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

    /// <summary>Devuelve si el mensaje quedo en la cola, que es lo que habilita el sellado posterior (COR-03).</summary>
    private async Task<bool> EncolarAsync(
        Conversacion conversacion, SalienteEncolado saliente, ContextoRegla contexto, string clave, CancellationToken ct)
    {
        var mensaje = await mensajes.EncolarSalienteAsync(
            conversacion.ConversacionId, saliente, clave, analistaId: null, contexto.CorrelationId, ct: ct);

        if (mensaje is null)
        {
            // Mismo envio decidido otra vez: el evento se esta reprocesando. Es el caso que la cola
            // existe para absorber, no un error. Cuenta como encolado: el mensaje esta en la cola.
            log.LogInformation(
                "El envio {Clave} de la conversacion {ConversacionId} ya estaba encolado. No se duplica.",
                clave, conversacion.ConversacionId);
        }

        return true;
    }
}
