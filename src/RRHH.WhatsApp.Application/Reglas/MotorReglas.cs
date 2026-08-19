using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas;

/// <summary>
/// Evalua las reglas registradas en orden de prioridad y acumula sus acciones. No envia mensajes
/// ni escribe en las tablas de otros modulos: devuelve decisiones y la capa de aplicacion las
/// ejecuta. Esa separacion es lo que permite probar el comportamiento del sistema sin base de
/// datos ni proveedor de WhatsApp.
/// </summary>
public sealed class MotorReglas(IEnumerable<IReglaNegocio> reglas, ILogger<MotorReglas> log) : IMotorReglas
{
    private readonly IReadOnlyList<IReglaNegocio> _reglas = [.. reglas.OrderBy(r => r.Prioridad)];

    public async Task<IReadOnlyList<AccionRegla>> ProcesarAsync(ContextoRegla contexto, CancellationToken ct = default)
    {
        var acciones = new List<AccionRegla>();

        foreach (var regla in _reglas)
        {
            ct.ThrowIfCancellationRequested();

            if (!regla.Aplica(contexto))
                continue;

            ResultadoRegla resultado;
            try
            {
                resultado = await regla.EvaluarAsync(contexto, ct);
            }
            catch (Exception ex)
            {
                // Una regla que revienta no debe tumbar el procesamiento del mensaje completo:
                // se registra y se sigue con las demas. El evento queda en la outbox para reintento.
                log.LogError(ex, "La regla {Codigo} fallo al evaluar. Correlation {CorrelationId}",
                    regla.Codigo, contexto.CorrelationId);
                continue;
            }

            if (resultado.Acciones.Count > 0)
            {
                log.LogDebug("Regla {Codigo} produjo {Cantidad} accion(es). Correlation {CorrelationId}",
                    regla.Codigo, resultado.Acciones.Count, contexto.CorrelationId);

                acciones.AddRange(resultado.Acciones);
            }

            if (resultado.DetenerEvaluacion)
            {
                log.LogDebug("Regla {Codigo} detuvo la evaluacion. Correlation {CorrelationId}",
                    regla.Codigo, contexto.CorrelationId);
                break;
            }
        }

        return acciones;
    }
}
