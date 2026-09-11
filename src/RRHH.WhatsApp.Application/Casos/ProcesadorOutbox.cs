using System.Text.Json;
using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Application.Reglas;
using RRHH.WhatsApp.Domain.Entidades;

namespace RRHH.WhatsApp.Application.Casos;

/// <summary>
/// Consume un evento de la outbox y lo convierte en una evaluacion de reglas. Es la contraparte de
/// <see cref="RecepcionWebhook"/>: el webhook encola y responde en milisegundos, y el trabajo real
/// ocurre aca, ya fuera del limite de 5 segundos que 360dialog da para recibir el 200.
/// </summary>
public sealed class ProcesadorOutbox(
    IFabricaContextoRegla fabrica,
    EvaluadorReglas evaluador,
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
            payload.ConversacionId, payload.IdBotonPulsado, evento.CorrelationId, ct);

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
            payload.ConversacionId, payload.HcId, evento.CorrelationId, ct);

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

        var contexto = await fabrica.ParaCambioEstadoPostulacionAsync(
            payload.PostulacionId, evento.CorrelationId, ct);

        var ejecutadas = await evaluador.EvaluarYEjecutarAsync(contexto, ct);

        log.LogInformation(
            "Descarte procesado sobre la postulacion {PostulacionId}: {Acciones} accion(es).",
            payload.PostulacionId, ejecutadas);
    }
    /// <summary>Lo que <see cref="RecepcionWebhook"/> deja en el payload y este procesador necesita.</summary>
    private sealed record PayloadMensajeEntrante(int ConversacionId, string? IdBotonPulsado);

    /// <summary>Lo que <see cref="RecepcionJobForms"/> deja en el payload.</summary>
    private sealed record PayloadJobFormsCompletado(int ConversacionId, int HcId);

    /// <summary>Lo que <see cref="AccionesBandeja"/> deja en el payload.</summary>
    private sealed record PayloadPostulacionDescartada(int PostulacionId);
}
