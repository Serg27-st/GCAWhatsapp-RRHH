using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Application.Reglas;
using RRHH.WhatsApp.Domain.Interfaces;

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
    IUnidadTrabajo unidad,
    TimeProvider reloj,
    ILogger<BarridoTiempo> log)
{
    public async Task<int> ProcesarConversacionAsync(int conversacionId, CancellationToken ct = default)
    {
        // V29: la clave del barrido solo evita duplicados dentro del mismo minuto (un reintento
        // inmediato del ciclo). Que un recordatorio no salga dos veces en barridos distintos lo
        // garantizan los sellos de cada regla, y por eso se confirman en la misma transaccion que el
        // mensaje encolado (V28): o quedan los dos, o ninguno y el proximo barrido lo intenta de nuevo.
        var clave = $"barrido:{conversacionId}:{reloj.GetUtcNow().UtcDateTime:yyyyMMddHHmm}";

        var ejecutadas = await unidad.EjecutarAsync(async c =>
        {
            var contexto = await fabrica.ParaTiempoTranscurridoAsync(conversacionId, clave, c);

            return await evaluador.EvaluarYEjecutarAsync(contexto, c);
        }, ct);

        if (ejecutadas > 0)
        {
            log.LogInformation(
                "Barrido por tiempo sobre la conversacion {ConversacionId}: {Acciones} accion(es).",
                conversacionId, ejecutadas);
        }

        return ejecutadas;
    }

    /// <summary>
    /// FUN-10 y FUN-11: el barrido por postulacion. Lo que vence es el proceso y no el hilo: una
    /// persona puede tener uno archivandose en una cuenta y otro vivo en otra.
    /// </summary>
    public async Task<int> ProcesarPostulacionAsync(int postulacionId, CancellationToken ct = default)
    {
        var clave = $"barrido:post:{postulacionId}:{reloj.GetUtcNow().UtcDateTime:yyyyMMddHHmm}";

        var ejecutadas = await unidad.EjecutarAsync(async c =>
        {
            var contexto = await fabrica.ParaTiempoPostulacionAsync(postulacionId, clave, c);

            return await evaluador.EvaluarYEjecutarAsync(contexto, c);
        }, ct);

        if (ejecutadas > 0)
        {
            log.LogInformation(
                "Barrido por tiempo sobre la postulacion {PostulacionId}: {Acciones} accion(es).",
                postulacionId, ejecutadas);
        }

        return ejecutadas;
    }
}
