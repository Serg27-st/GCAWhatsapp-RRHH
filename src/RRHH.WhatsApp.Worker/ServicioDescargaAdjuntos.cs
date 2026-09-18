using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Domain.Entidades;

namespace RRHH.WhatsApp.Worker;

/// <summary>
/// Baja, escanea y guarda los archivos que mandan los postulantes (V33, ARQ-10).
/// <para>
/// Bucle propio porque su ritmo es otro: una descarga puede tardar minutos entre la red y el
/// antivirus, y eso no puede atrasar ni el despacho de mensajes ni la evaluacion de reglas. Y no puede
/// esperar al barrido de minutos: el id de medio caduca.
/// </para>
/// </summary>
public sealed class ServicioDescargaAdjuntos(
    IServiceScopeFactory ambitos,
    IOptions<OpcionesWorker> opciones,
    ILogger<ServicioDescargaAdjuntos> log) : BackgroundService
{
    private readonly OpcionesWorker _opciones = opciones.Value;

    /// <summary>Que hizo el ultimo ciclo. Es lo que distingue "vivo" de "vivo y trabajando".</summary>
    private string? _detalle;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var parametros = new ParametrosDescargaAdjuntos(
            _opciones.TamanoLoteDescargaAdjuntos,
            _opciones.IntentosDescargaAdjunto,
            TimeSpan.FromSeconds(_opciones.EsperaReintentoDescargaSegundos),
            TimeSpan.FromSeconds(_opciones.TiempoMaximoDescargaSegundos));

        log.LogInformation(
            "Descarga de adjuntos iniciada: cada {Intervalo}s, hasta {Lote} archivo(s) por vuelta.",
            _opciones.IntervaloDescargaAdjuntosSegundos, parametros.TamanoLote);

        while (!ct.IsCancellationRequested)
        {
            var lleno = false;

            try
            {
                // Un ambito por lote: el DbContext no acumula entidades entre vueltas.
                using var ambito = ambitos.CreateScope();

                var resumen = await ambito.ServiceProvider
                    .GetRequiredService<DescargaAdjuntos>()
                    .ProcesarAsync(parametros, ct);

                _detalle = resumen.ToString();
                lleno = resumen.Intentados >= parametros.TamanoLote;

                if (resumen.Intentados > 0)
                    log.LogInformation("Descarga de adjuntos: {Resumen}", resumen);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Un fallo del lote —la base, casi siempre— no mata el bucle: lo pendiente sigue
                // pendiente y se retoma en la proxima vuelta.
                log.LogError(ex, "Fallo el lote de descarga de adjuntos. Se retoma en la proxima vuelta.");

                await EsperaSegura.DormirAsync(TimeSpan.FromSeconds(_opciones.PausaTrasErrorSegundos), ct);
            }

            await Latido.RegistrarAsync(
                ambitos, log, ServiciosVigilados.DescargaAdjuntos, Tolerancia(), _detalle, ct);

            // Con el lote lleno hay archivos esperando: se sigue sin dormir.
            if (!lleno)
                await EsperaSegura.DormirAsync(TimeSpan.FromSeconds(_opciones.IntervaloDescargaAdjuntosSegundos), ct);
        }

        log.LogInformation("Descarga de adjuntos detenida.");
    }

    /// <summary>
    /// Tres ciclos de holgura, mas lo que puede durar un lote entero en el peor caso legitimo: cada
    /// archivo hasta su tiempo maximo.
    /// </summary>
    private TimeSpan Tolerancia() =>
        TimeSpan.FromSeconds(
            Math.Max(_opciones.IntervaloDescargaAdjuntosSegundos, _opciones.PausaTrasErrorSegundos) * 3
            + (long)_opciones.TamanoLoteDescargaAdjuntos * _opciones.TiempoMaximoDescargaSegundos);
}
