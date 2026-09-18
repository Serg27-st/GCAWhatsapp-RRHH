using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Application.Casos;

/// <summary>
/// Tipos de evento que se publican en la outbox. Constantes para no repartir cadenas sueltas.
/// <para>
/// Solo los que tienen consumidor (V9, V32). Los avisos para una persona —plantilla sin aprobar,
/// vacante sin formulario, menu sin opciones— son <c>AlertasOperativas</c>: como eventos
/// quedaban pendientes para siempre, sin nadie que los leyera (M1).
/// </para>
/// </summary>
public static class TiposEvento
{
    public const string MensajeEntranteRecibido = "MensajeEntranteRecibido";

    /// <summary>La bandeja lo consume por SignalR; el evento sobrevive si el analista no esta conectado.</summary>
    public const string AnalistaNotificado = "AnalistaNotificado";

    /// <summary>Regla 12: una postulacion quedo descartada y corresponde el cierre de cortesia.</summary>
    public const string PostulacionDescartada = "PostulacionDescartada";

    /// <summary>Regla 9: el postulante completo el formulario. Lo consume el motor de reglas.</summary>
    public const string JobFormsCompletado = "JobFormsCompletado";
}

public sealed record ResultadoRecepcion(
    bool FirmaValida,
    int MensajesNuevos,
    int MensajesDuplicados,
    int EstadosActualizados)
{
    public static readonly ResultadoRecepcion FirmaRechazada = new(false, 0, 0, 0);
}

/// <summary>
/// Gateway de mensajeria: recibe el webhook, valida la firma, normaliza y encola. No evalua reglas
/// ni envia nada.
/// <para>
/// La razon es concreta: 360dialog tiene un limite duro de 5 segundos para recibir un 200, y si no
/// lo recibe reintenta la entrega. Procesar las reglas aca haria que un pico de trafico se
/// convirtiera en reintentos, que es justo el patron de carga que conviene evitar.
/// </para>
/// </summary>
public sealed class RecepcionWebhook(
    IWhatsAppProvider proveedor,
    IConversacionService conversaciones,
    IMensajeService mensajes,
    IEventoSistemaService eventos,
    IUnidadTrabajo unidad,
    ILogger<RecepcionWebhook> log)
{
    /// <summary>Lo que paso con un entrante dentro de su transaccion.</summary>
    private enum Ingesta { Nuevo, Duplicado }

    public async Task<ResultadoRecepcion> ProcesarAsync(
        string cuerpoCrudo,
        IReadOnlyDictionary<string, string> cabeceras,
        CancellationToken ct = default)
    {
        // Primero la firma, antes de mirar el cuerpo. Un payload que no se puede verificar no se
        // procesa "por si acaso": se descarta.
        if (!proveedor.ValidarFirma(cuerpoCrudo, cabeceras))
            return ResultadoRecepcion.FirmaRechazada;

        var entrantes = proveedor.InterpretarWebhook(cuerpoCrudo);
        var acuses = proveedor.InterpretarEstados(cuerpoCrudo);

        var nuevos = 0;
        var duplicados = 0;

        foreach (var dto in entrantes)
        {
            var correlationId = Guid.NewGuid();

            // Fuera de la transaccion: ya es idempotente y resuelve la carrera del indice unico por
            // telefono. Un hilo creado sin mensaje no es un estado a medias, es un hilo vacio.
            var conversacion = await conversaciones.ObtenerOCrearAsync(dto.TelefonoE164, ct);

            // C6, COR-05: mensaje, ventana y evento se confirman juntos. Antes quedaba el mensaje
            // sin evento si la publicacion fallaba: Meta reentregaba, el duplicado se descartaba por
            // su ProviderMessageId y el evento no llegaba a existir, asi que nadie respondia. Si
            // algo lanza, no queda nada, el controlador responde 500 y Meta reentrega.
            var ingesta = await unidad.EjecutarAsync(async c =>
            {
                // C1, A13: la actividad previa se lee ANTES de registrar el entrante. Despues ya no
                // existe: RegistrarEntradaAsync la mueve al presente, y la Regla 9 dejaria de ver los
                // dias de silencio que la hacen repreguntar la empresa (ARQ-07).
                var fechaActividadAnterior = conversacion.FechaUltimaActividad;

                var mensaje = await mensajes.RegistrarEntranteAsync(
                    conversacion.ConversacionId, dto, correlationId, c);

                // Nulo significa que Meta reintrego un mensaje que ya teniamos. No se vuelve a
                // abrir la ventana de 24h ni se vuelve a encolar: seria procesarlo dos veces.
                if (mensaje is null)
                    return Ingesta.Duplicado;

                // V33: el archivo se registra con su mensaje. El id de medio caduca, y una reentrega ya
                // no lo traeria: el mensaje se descartaria por duplicado.
                if (dto.Medio is { } medio)
                    await mensajes.RegistrarAdjuntoAsync(mensaje.MensajeId, medio, c);

                await conversaciones.RegistrarEntradaAsync(conversacion.ConversacionId, dto.FechaUtc, c);

                // ARQ-13 (AL6): lo minimo para volver a leer el mensaje. Sin telefono, sin texto y sin
                // nombre de perfil: eso vive en las tablas, que se purgan (Regla 17); la outbox no puede
                // ser un segundo almacen de datos personales que nadie limpia.
                await eventos.PublicarAsync(TiposEvento.MensajeEntranteRecibido, new
                {
                    conversacion.ConversacionId,
                    mensaje.MensajeId,
                    dto.IdBotonPulsado,
                    FechaActividadAnterior = fechaActividadAnterior
                }, correlationId, c);

                return Ingesta.Nuevo;
            }, ct);

            if (ingesta == Ingesta.Nuevo)
                nuevos++;
            else
                duplicados++;
        }

        // Cada acuse en su propia transaccion: uno que falla no tiene por que deshacer los demas,
        // y la reentrega de Meta vuelve a traerlo.
        foreach (var acuse in acuses)
            await unidad.EjecutarAsync(c => AplicarAcuseAsync(acuse, c), ct);

        if (nuevos > 0 || duplicados > 0 || acuses.Count > 0)
        {
            log.LogInformation(
                "Webhook procesado: {Nuevos} nuevos, {Duplicados} duplicados, {Acuses} acuses.",
                nuevos, duplicados, acuses.Count);
        }

        return new ResultadoRecepcion(true, nuevos, duplicados, acuses.Count);
    }

    /// <summary>
    /// FUN-13 (M2): un mensaje que Meta acepto y despues no entrego era invisible. El analista creia
    /// haber citado al postulante y el postulante nunca se enteraba. El aviso sale en la misma
    /// transaccion que el estado: o quedan los dos, o ninguno y Meta reentrega.
    /// </summary>
    private async Task AplicarAcuseAsync(EstadoEntregaDto acuse, CancellationToken ct)
    {
        var resultado = await mensajes.ActualizarEstadoEntregaAsync(acuse, ct);

        if (resultado is not { PasoAFallido: true })
            return;

        if (resultado.AnalistaId is not { } analistaId)
        {
            // Un mensaje del bot a un hilo que todavia no es de nadie: no hay a quien avisar. Queda
            // el estado en el mensaje y el registro del servicio.
            log.LogWarning(
                "El mensaje {MensajeId} no llego y la conversacion {ConversacionId} no tiene analista a quien avisar.",
                resultado.MensajeId, resultado.ConversacionId);

            return;
        }

        // Identificadores y un motivo generico, no el texto del mensaje: la outbox no guarda datos
        // personales (ARQ-13, AL6).
        await eventos.PublicarAsync(TiposEvento.AnalistaNotificado, new
        {
            AnalistaId = analistaId,
            resultado.ConversacionId,
            resultado.MensajeId,
            Mensaje = $"Un mensaje no llego al postulante: {MotivoCorto(acuse)}."
        }, Guid.NewGuid(), ct);
    }

    /// <summary>
    /// El analista no tiene por que conocer los codigos de Meta. Los que cambian lo que conviene hacer
    /// van en sus palabras; el resto, con lo que informo Meta.
    /// </summary>
    private static string MotivoCorto(EstadoEntregaDto acuse) => acuse.CodigoError switch
    {
        // Re-engagement: solo sale con plantilla aprobada hasta que el postulante vuelva a escribir.
        "131047" => "pasaron mas de 24 horas desde su ultimo mensaje y hace falta una plantilla aprobada",
        "131026" => "WhatsApp no pudo entregarlo a ese numero",
        _ when !string.IsNullOrWhiteSpace(acuse.DescripcionError) => Recortar(acuse.DescripcionError.Trim(), 120),
        _ => "Meta no informo el motivo"
    };

    private static string Recortar(string texto, int maximo) =>
        texto.Length <= maximo ? texto : texto[..maximo];
}
