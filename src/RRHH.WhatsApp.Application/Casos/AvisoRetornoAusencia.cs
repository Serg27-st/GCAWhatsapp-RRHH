using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Application.Casos;

/// <summary>
/// FUN-12 (A10): cuando termina una ausencia, el titular recibe un resumen de lo que quedo con su
/// respaldo mientras no estaba.
/// <para>
/// Sin el, quien vuelve de vacaciones retoma su bandeja sin saber que hay conversaciones nuevas de
/// sus cuentas atendidas por otra persona, y nadie las devuelve ni las coordina. No es una regla: no
/// decide nada sobre el postulante, solo informa al analista.
/// </para>
/// </summary>
public sealed class AvisoRetornoAusencia(
    IAusenciaService ausencias,
    ICuentaService cuentas,
    IConversacionService conversaciones,
    IEventoSistemaService eventos,
    IUnidadTrabajo unidad,
    TimeProvider reloj,
    ILogger<AvisoRetornoAusencia> log)
{
    /// <summary>Devuelve cuantas ausencias terminadas se procesaron.</summary>
    public async Task<int> ProcesarAsync(CancellationToken ct = default)
    {
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var finalizadas = await ausencias.ListarFinalizadasSinAvisoAsync(ahora, ct);

        foreach (var ausencia in finalizadas)
        {
            // Cada ausencia con su transaccion: el aviso y su sello se confirman juntos (P4), y una
            // que falla no impide avisar a los demas titulares.
            await unidad.EjecutarAsync(async c =>
            {
                var titularId = ausencia.AnalistaId;

                var propias = (await cuentas.ListarConDotacionAsync(c))
                    .Where(d => d.Titular?.AnalistaId == titularId)
                    .ToList();

                var cantidad = await conversaciones.ContarAsignadasPorAusenciaAsync(
                    [.. propias.Select(d => d.Cuenta.CuentaId)],
                    titularId,
                    ausencia.FechaInicio,
                    ausencia.FechaFin,
                    c);

                // Sin nada que contar, el aviso seria ruido. Se sella igual: esta ausencia ya se miro.
                if (cantidad > 0)
                {
                    var respaldos = propias
                        .Select(d => d.Respaldo?.Nombre)
                        .Where(n => n is not null)
                        .Distinct()
                        .ToList();

                    var quien = respaldos.Count > 0 ? string.Join(" y ", respaldos) : "tu respaldo";
                    var plural = cantidad == 1 ? "conversacion nueva quedo" : "conversaciones nuevas quedaron";

                    await eventos.PublicarAsync(TiposEvento.AnalistaNotificado, new
                    {
                        AnalistaId = titularId,
                        Mensaje = $"Durante tu ausencia, {cantidad} {plural} con {quien}."
                    }, Guid.NewGuid(), c);
                }

                await ausencias.MarcarAvisoRetornoAsync(ausencia.AusenciaId, c);
            }, ct);
        }

        if (finalizadas.Count > 0)
            log.LogInformation("Avisos de retorno procesados: {Cantidad} ausencia(s).", finalizadas.Count);

        return finalizadas.Count;
    }
}
