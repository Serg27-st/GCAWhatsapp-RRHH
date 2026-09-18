using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas.Implementaciones;

/// <summary>
/// Regla 8 — vencimiento de una transferencia (FUN-07, A1, M5).
/// <para>
/// Una transferencia que nadie responde no puede quedar pendiente para siempre: mientras lo esta,
/// ninguna otra transferencia de esa conversacion es posible y el hilo queda en un limbo entre dos
/// analistas (V21). Cumplido el plazo, vuelve a quien la envio y los dos se enteran.
/// </para>
/// </summary>
public sealed class R08VencimientoTransferencia : IReglaNegocio
{
    public string Codigo => "R08";

    public string Descripcion => "Vence la transferencia no urgente que nadie respondio en el plazo.";

    /// <summary>Antes del escalamiento: quien tiene que responderle al postulante depende de esto.</summary>
    public int Prioridad => 21;

    public bool Aplica(ContextoRegla ctx) =>
        ctx.Disparador == TipoDisparador.TiempoTranscurrido
        && ctx.TransferenciaPendiente is { Urgente: false, FechaVencimiento: not null };

    public Task<ResultadoRegla> EvaluarAsync(ContextoRegla ctx, CancellationToken ct = default)
    {
        var transferencia = ctx.TransferenciaPendiente!;

        if (ctx.AhoraUtc < transferencia.FechaVencimiento)
            return Task.FromResult(ResultadoRegla.SinAccion);

        return Task.FromResult(ResultadoRegla.Con(
            new VencerTransferencia(transferencia.TransferenciaId),

            // Los dos tienen que saberlo: el origen porque el hilo sigue siendo suyo, y el destino
            // porque lo que le ofrecieron ya no esta esperando su respuesta.
            new NotificarAnalista(
                transferencia.AnalistaOrigenId,
                "La transferencia vencio sin respuesta: la conversacion sigue a tu cargo."),
            new NotificarAnalista(
                transferencia.AnalistaDestinoId,
                "La transferencia que tenias pendiente vencio y volvio a quien te la envio."),

            new RegistrarAuditoria(
                "TransferenciaVencida",
                $"Transferencia {transferencia.TransferenciaId} vencida sin respuesta.")));
    }
}
