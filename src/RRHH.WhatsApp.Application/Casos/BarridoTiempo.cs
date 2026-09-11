using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Application.Reglas;

namespace RRHH.WhatsApp.Application.Casos;

/// <summary>
/// Evalua una conversacion contra el paso del tiempo, que es el disparador de la Regla 2.
/// <para>
/// Trabaja de a una conversacion a proposito: el Worker le da a cada una su propio ambito, para que
/// un hilo que falle no arrastre al resto del barrido ni acumule entidades en el DbContext.
/// </para>
/// </summary>
public sealed class BarridoTiempo(
    IFabricaContextoRegla fabrica,
    EvaluadorReglas evaluador,
    ILogger<BarridoTiempo> log)
{
    public async Task<int> ProcesarConversacionAsync(int conversacionId, CancellationToken ct = default)
    {
        var contexto = await fabrica.ParaTiempoTranscurridoAsync(conversacionId, ct);

        var ejecutadas = await evaluador.EvaluarYEjecutarAsync(contexto, ct);

        if (ejecutadas > 0)
        {
            log.LogInformation(
                "Barrido por tiempo sobre la conversacion {ConversacionId}: {Acciones} accion(es).",
                conversacionId, ejecutadas);
        }

        return ejecutadas;
    }
}
