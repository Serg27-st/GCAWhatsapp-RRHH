using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Worker;

/// <summary>
/// Regla 17 — retencion de datos. Borra los CVs que cumplieron el plazo y limpia su referencia,
/// alineado a la Ley de Proteccion de Datos Personales.
/// <para>
/// Corre aparte del barrido de reglas y con su propio ritmo, porque no decide nada sobre una
/// conversacion: es mantenimiento de datos, y basta con que pase una vez al dia (Seccion 9.6.5).
/// </para>
/// </summary>
public sealed class ServicioPurgaCv(
    IServiceScopeFactory ambitos,
    IOptions<OpcionesWorker> opciones,
    ILogger<ServicioPurgaCv> log) : BackgroundService
{
    /// <summary>Coincide con la semilla de datos.retencion_cv_dias; solo aplica si falta la clave.</summary>
    private const int RetencionPorDefecto = 365;

    private readonly OpcionesWorker _opciones = opciones.Value;

    /// <summary>Que hizo el ultimo ciclo. Es lo que distingue "vivo" de "vivo y trabajando".</summary>
    private string? _detalle;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        log.LogInformation("Purga de CVs iniciada: cada {Intervalo} hora(s).", _opciones.IntervaloPurgaHoras);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var purgados = await PurgarAsync(ct);

                _detalle = $"{purgados} CV(s) purgados en el ultimo ciclo.";

                if (purgados > 0)
                    log.LogInformation("Purga de CVs: {Purgados} archivo(s) eliminados por retencion.", purgados);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Fallo la purga de CVs. Se reintenta en el proximo ciclo.");
            }

            await Latido.RegistrarAsync(
                ambitos, log, ServiciosVigilados.PurgaCv,
                TimeSpan.FromHours(_opciones.IntervaloPurgaHoras * 3),
                _detalle, ct);

            await EsperaSegura.DormirAsync(TimeSpan.FromHours(_opciones.IntervaloPurgaHoras), ct);
        }

        log.LogInformation("Purga de CVs detenida.");
    }

    private async Task<int> PurgarAsync(CancellationToken ct)
    {
        IReadOnlyList<int> respuestas;

        using (var ambito = ambitos.CreateScope())
        {
            var sp = ambito.ServiceProvider;

            var configuracion = await sp.GetRequiredService<IConfiguracionReglasService>()
                .ObtenerTodasAsync(ct);

            var dias = configuracion.TryGetValue(ClavesConfiguracion.RetencionCvDias, out var valor)
                && int.TryParse(valor, out var n) && n > 0
                    ? n
                    : RetencionPorDefecto;

            var vencidas = await sp.GetRequiredService<IJobFormsService>()
                .ListarCvsPorPurgarAsync(dias, _opciones.TamanoLotePurga, ct);

            respuestas = [.. vencidas.Select(r => r.RespuestaId)];
        }

        var purgados = 0;

        foreach (var respuestaId in respuestas)
        {
            if (ct.IsCancellationRequested)
                break;

            using var ambito = ambitos.CreateScope();

            try
            {
                await ambito.ServiceProvider
                    .GetRequiredService<IJobFormsService>()
                    .PurgarCvAsync(respuestaId, ct);

                purgados++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Un archivo que no se puede borrar —permisos, recurso compartido caido— no debe
                // frenar al resto: vuelve a entrar solo en el proximo ciclo.
                log.LogError(ex, "Fallo la purga del CV de la respuesta {RespuestaId}.", respuestaId);
            }
        }

        return purgados;
    }
}
