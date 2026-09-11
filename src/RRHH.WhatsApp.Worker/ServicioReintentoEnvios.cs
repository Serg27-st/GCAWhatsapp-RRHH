using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Domain.Entidades;

namespace RRHH.WhatsApp.Worker;

/// <summary>
/// Reintenta los salientes que el proveedor rechazo sin llegar a procesarlos.
/// <para>
/// Sin este bucle, un error pasajero de Meta —un 503, un corte de red— deja al postulante sin la
/// respuesta del bot y nada vuelve a intentarlo: el mensaje queda fallido en la base y solo lo
/// nota quien mire la bandeja. Con el, lo transitorio se recupera solo y lo demas queda visible.
/// </para>
/// <para>
/// Corre aparte del consumidor de la outbox porque su ritmo es otro: el retroceso entre intentos
/// se mide en minutos, no en segundos, y mezclarlo con la cola haria que un proveedor caido
/// frenara tambien el procesamiento de los mensajes entrantes.
/// </para>
/// </summary>
public sealed class ServicioReintentoEnvios(
    IServiceScopeFactory ambitos,
    IOptions<OpcionesWorker> opciones,
    ILogger<ServicioReintentoEnvios> log) : BackgroundService
{
    private readonly OpcionesWorker _opciones = opciones.Value;

    /// <summary>Que hizo el ultimo ciclo. Es lo que distingue "vivo" de "vivo y trabajando".</summary>
    private string? _detalle;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        log.LogInformation(
            "Reintento de envios iniciado: cada {Intervalo} minuto(s), hasta {Lote} mensaje(s) por vuelta.",
            _opciones.IntervaloReintentoMinutos, _opciones.TamanoLoteReintento);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var ambito = ambitos.CreateScope();

                var reintentos = ambito.ServiceProvider.GetRequiredService<ReintentoEnvios>();
                var resumen = await reintentos.ProcesarAsync(_opciones.TamanoLoteReintento, ct);

                _detalle = resumen.ToString();

                if (resumen.Intentados > 0)
                    log.LogInformation("Reintento de envios: {Resumen}", resumen);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Un fallo del ciclo completo no puede matar el bucle: los mensajes siguen en la
                // base con su ProximoIntentoUtc y la vuelta siguiente los toma igual.
                log.LogError(ex, "Fallo el ciclo de reintentos. Se retoma en la proxima vuelta.");

                await EsperaSegura.DormirAsync(TimeSpan.FromSeconds(_opciones.PausaTrasErrorSegundos), ct);
            }

            await Latido.RegistrarAsync(
                ambitos, log, ServiciosVigilados.ReintentoEnvios,
                TimeSpan.FromMinutes(_opciones.IntervaloReintentoMinutos * 3),
                _detalle, ct);

            await EsperaSegura.DormirAsync(TimeSpan.FromMinutes(_opciones.IntervaloReintentoMinutos), ct);
        }

        log.LogInformation("Reintento de envios detenido.");
    }
}
