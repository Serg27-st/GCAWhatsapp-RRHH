using System.Text.Json;
using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Application.Reglas;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Application.Casos;

/// <summary>
/// Consume un evento de la outbox y lo convierte en una evaluacion de reglas. Es la contraparte de
/// <see cref="RecepcionWebhook"/>: el webhook encola y responde en milisegundos, y el trabajo real
/// ocurre aca, ya fuera del limite de 5 segundos que 360dialog da para recibir el 200.
/// </summary>
public sealed class ProcesadorOutbox(
    IFabricaContextoRegla fabrica,
    EvaluadorReglas evaluador,
    IPostulacionService postulaciones,
    IConfiguracionReglasService configuracion,
    ILogger<ProcesadorOutbox> log)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Tipos que este procesador sabe atender, y los unicos que pide de la cola.
    /// <para>
    /// Los demas que publica <see cref="TiposEvento"/> esperan al servicio que los va a consumir
    /// —la bandeja por SignalR, sobre todo— y se quedan Pendientes hasta entonces. Pedirlos sin
    /// saber que hacer con ellos los dejaria fallando en ciclo; ignorarlos sin filtrar la consulta
    /// los dejaria taponando la cabeza de la cola.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyCollection<string> TiposQueAtiende =
        [TiposEvento.MensajeEntranteRecibido, TiposEvento.JobFormsCompletado, TiposEvento.PostulacionDescartada];

    public async Task ProcesarAsync(EventoSistema evento, CancellationToken ct = default)
    {
        switch (evento.Tipo)
        {
            case TiposEvento.MensajeEntranteRecibido:
                await ProcesarMensajeEntranteAsync(evento, ct);
                break;

            case TiposEvento.JobFormsCompletado:
                await ProcesarJobFormsCompletadoAsync(evento, ct);
                break;

            case TiposEvento.PostulacionDescartada:
                await ProcesarPostulacionDescartadaAsync(evento, ct);
                break;

            default:
                // No deberia ocurrir: la cola se consulta filtrando por TiposQueAtiende. Si ocurre,
                // es un desajuste entre ese filtro y este switch, y conviene verlo, no taparlo.
                throw new InvalidOperationException(
                    $"El evento {evento.EventoId} es de tipo '{evento.Tipo}', que este procesador no atiende.");
        }
    }

    private async Task ProcesarMensajeEntranteAsync(EventoSistema evento, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<PayloadMensajeEntrante>(evento.Payload, Json)
            ?? throw new InvalidOperationException($"El evento {evento.EventoId} no trae un payload legible.");

        if (payload.ConversacionId <= 0)
        {
            throw new InvalidOperationException(
                $"El evento {evento.EventoId} no trae ConversacionId. Payload: {evento.Payload}");
        }

        var contexto = await fabrica.ParaMensajeEntranteAsync(
            payload.ConversacionId, payload.MensajeId, payload.IdBotonPulsado,
            payload.FechaActividadAnterior, evento.CorrelationId, ClaveDe(evento), ct);

        var ejecutadas = await evaluador.EvaluarYEjecutarAsync(contexto, ct);

        log.LogInformation(
            "Evento {EventoId} procesado sobre la conversacion {ConversacionId}: {Acciones} accion(es). Correlation {CorrelationId}",
            evento.EventoId, payload.ConversacionId, ejecutadas, evento.CorrelationId);
    }


    private async Task ProcesarJobFormsCompletadoAsync(EventoSistema evento, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<PayloadJobFormsCompletado>(evento.Payload, Json)
            ?? throw new InvalidOperationException($"El evento {evento.EventoId} no trae un payload legible.");

        if (payload.ConversacionId <= 0 || payload.HcId <= 0)
        {
            throw new InvalidOperationException(
                $"El evento {evento.EventoId} no identifica la conversacion o la vacante. Payload: {evento.Payload}");
        }

        var contexto = await fabrica.ParaJobFormsCompletadoAsync(
            payload.ConversacionId, payload.HcId, evento.CorrelationId, ClaveDe(evento), ct);

        var ejecutadas = await evaluador.EvaluarYEjecutarAsync(contexto, ct);

        log.LogInformation(
            "JobForms completado procesado sobre la conversacion {ConversacionId}: {Acciones} accion(es).",
            payload.ConversacionId, ejecutadas);
    }

    private async Task ProcesarPostulacionDescartadaAsync(EventoSistema evento, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<PayloadPostulacionDescartada>(evento.Payload, Json)
            ?? throw new InvalidOperationException($"El evento {evento.EventoId} no trae un payload legible.");

        if (payload.PostulacionId <= 0)
        {
            throw new InvalidOperationException(
                $"El evento {evento.EventoId} no identifica la postulacion. Payload: {evento.Payload}");
        }

        // FUN-10 (A11): lo que el analista pidio se registra antes de evaluar, y en la misma
        // transaccion que la evaluacion: si algo falla despues, el pedido no queda escrito a medias.
        await postulaciones.MarcarCierrePendienteAsync(
            payload.PostulacionId,
            payload.EnviarCierre,
            await CierreAutomaticoAsync(ct),
            ct);

        var contexto = await fabrica.ParaCambioEstadoPostulacionAsync(
            payload.PostulacionId, evento.CorrelationId, ClaveDe(evento), ct);

        var ejecutadas = await evaluador.EvaluarYEjecutarAsync(contexto, ct);

        log.LogInformation(
            "Descarte procesado sobre la postulacion {PostulacionId}: {Acciones} accion(es).",
            payload.PostulacionId, ejecutadas);
    }

    /// <summary>
    /// A11: la operacion entera puede apagar el cierre automatico desde la configuracion. Por defecto
    /// sale: es lo que el dossier pide para que nadie quede sin respuesta tras un descarte.
    /// </summary>
    private async Task<bool> CierreAutomaticoAsync(CancellationToken ct)
    {
        var config = await configuracion.ObtenerTodasAsync(ct);

        return !config.TryGetValue(ClavesConfiguracion.CierreAutomatico, out var valor)
            || !bool.TryParse(valor, out var automatico)
            || automatico;
    }

    /// <summary>
    /// V29: la identidad del evento es la base de las claves de envio. Si el evento se reprocesa
    /// —fallo algo despues de encolar—, las reglas vuelven a decidir lo mismo con las mismas claves y
    /// la cola no acepta el mensaje repetido.
    /// </summary>
    private static string ClaveDe(EventoSistema evento) => $"evt:{evento.EventoId}";

    /// <summary>
    /// Lo que <see cref="RecepcionWebhook"/> deja en el payload y este procesador necesita. Son ids y una
    /// fecha: el contenido del mensaje lo relee la fabrica de la tabla (ARQ-13).
    /// </summary>
    private sealed record PayloadMensajeEntrante(
        int ConversacionId, long MensajeId, string? IdBotonPulsado, DateTime? FechaActividadAnterior);

    /// <summary>Lo que <see cref="RecepcionJobForms"/> deja en el payload.</summary>
    private sealed record PayloadJobFormsCompletado(int ConversacionId, int HcId);

    /// <summary>
    /// Lo que <see cref="AccionesBandeja"/> deja en el payload. <c>EnviarCierre</c> es lo que el
    /// analista marco en el dialogo de descarte (FUN-10, A11).
    /// </summary>
    private sealed record PayloadPostulacionDescartada(int PostulacionId, bool EnviarCierre = true);
}
