using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Domain.Entidades;

namespace RRHH.WhatsApp.Worker;

/// <summary>
/// Saca de la cola lo que decidieron las reglas y lo envia (V29, ARQ-03).
/// <para>
/// Corre aparte del consumidor de la outbox porque es la mitad que toca la red: si Meta responde
/// lento o el limitador frena, eso no puede atrasar la evaluacion de los mensajes que siguen
/// entrando. Y aparte del reintento porque su ritmo es de segundos: un postulante que eligio una
/// empresa espera el enlace ya, no en el proximo ciclo de minutos.
/// </para>
/// </summary>
public sealed class ServicioDespachoEnvios(
    IServiceScopeFactory ambitos,
    IOptions<OpcionesWorker> opciones,
    ILogger<ServicioDespachoEnvios> log) : BackgroundService
{
    private readonly OpcionesWorker _opciones = opciones.Value;

    /// <summary>Que hizo el ultimo ciclo. Es lo que distingue "vivo" de "vivo y trabajando".</summary>
    private string? _detalle;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        log.LogInformation(
            "Despacho de envios iniciado: cada {Intervalo}s, hasta {Lote} mensaje(s) por vuelta.",
            _opciones.IntervaloDespachoSegundos, _opciones.TamanoLoteDespacho);

        while (!ct.IsCancellationRequested)
        {
            var lleno = false;

            try
            {
                // Un ambito por lote: el DbContext no acumula entidades entre vueltas.
                using var ambito = ambitos.CreateScope();

                var resumen = await ambito.ServiceProvider
                    .GetRequiredService<DespachoEnvios>()
                    .ProcesarAsync(
                        _opciones.TamanoLoteDespacho,
                        TimeSpan.FromSeconds(_opciones.TimeoutEnviandoSegundos),
                        ct);

                _detalle = resumen.ToString();
                lleno = resumen.Intentados >= _opciones.TamanoLoteDespacho;

                if (resumen.Intentados > 0 || resumen.Recuperados > 0)
                    log.LogInformation("Despacho de envios: {Resumen}", resumen);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Un fallo del lote no mata el bucle: lo tomado queda Enviando y, si no se resuelve,
                // la vuelta que supere el timeout lo marca ambiguo en vez de reenviarlo.
                log.LogError(ex, "Fallo el lote de despacho. Se retoma en la proxima vuelta.");

                await EsperaSegura.DormirAsync(TimeSpan.FromSeconds(_opciones.PausaTrasErrorSegundos), ct);
            }

            await Latido.RegistrarAsync(
                ambitos, log, ServiciosVigilados.DespachoEnvios, Tolerancia(), _detalle, ct);

            // Con el lote lleno hay cola acumulada: se sigue sin dormir. El limitador del adaptador
            // es el que marca el ritmo real hacia Meta.
            if (!lleno)
                await EsperaSegura.DormirAsync(TimeSpan.FromSeconds(_opciones.IntervaloDespachoSegundos), ct);
        }

        log.LogInformation("Despacho de envios detenido.");
    }

    /// <summary>Tres ciclos de holgura, contando la pausa tras un error y la espera de un Enviando.</summary>
    private TimeSpan Tolerancia() =>
        TimeSpan.FromSeconds(Math.Max(
            Math.Max(_opciones.IntervaloDespachoSegundos, _opciones.PausaTrasErrorSegundos),
            _opciones.TimeoutEnviandoSegundos) * 3);
}
