using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Casos;

/// <summary>
/// Une las dos mitades del motor: le pide al <see cref="IMotorReglas"/> que decida y le entrega
/// lo decidido al <see cref="EjecutorAcciones"/>.
/// <para>
/// Es deliberadamente delgada, pero es la unica puerta: tanto la outbox como el barrido por tiempo
/// pasan por aca, de modo que una regla se comporta igual venga de un mensaje entrante o de un tick
/// del Worker.
/// </para>
/// </summary>
public sealed class EvaluadorReglas(
    IMotorReglas motor,
    EjecutorAcciones ejecutor,
    ILogger<EvaluadorReglas> log)
{
    /// <summary>Cuantas acciones se ejecutaron. Cero significa que ninguna regla aplico.</summary>
    public async Task<int> EvaluarYEjecutarAsync(ContextoRegla contexto, CancellationToken ct = default)
    {
        var acciones = await motor.ProcesarAsync(contexto, ct);

        if (acciones.Count == 0)
        {
            log.LogDebug("Ninguna regla produjo acciones. Correlation {CorrelationId}", contexto.CorrelationId);
            return 0;
        }

        await ejecutor.EjecutarAsync(acciones, contexto, ct);

        return acciones.Count;
    }
}
