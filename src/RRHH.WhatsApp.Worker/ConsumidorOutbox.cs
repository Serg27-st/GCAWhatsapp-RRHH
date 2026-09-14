using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Worker;

/// <summary>
/// Consume la outbox: saca los eventos pendientes que el <see cref="ProcesadorOutbox"/> sabe
/// atender y los procesa de a uno. Es la otra mitad del gateway — el webhook encola y responde 200
/// en milisegundos, y el trabajo de reglas ocurre aca.
/// <para>
/// Asume una sola instancia del Worker, y la <see cref="GuardiaInstancia"/> la garantiza: este bucle
/// no arranca hasta que el proceso tiene el candado (V24). La tabla de eventos no tiene reserva ni
/// bloqueo por fila, asi que dos procesos leyendo la misma cola tomarian el mismo evento y podrian
/// enviar dos veces el mismo mensaje de WhatsApp. Si alguna vez hace falta mas de una instancia
/// activa, primero hay que agregar la reserva.
/// </para>
/// </summary>
public sealed class ConsumidorOutbox(
    IServiceScopeFactory ambitos,
    IOptions<OpcionesWorker> opciones,
    ILogger<ConsumidorOutbox> log) : BackgroundService
{
    /// <summary>Coincide con la semilla de outbox.reintentos_maximos; solo aplica si falta la clave.</summary>
    private const int ReintentosPorDefecto = 5;

    private readonly OpcionesWorker _opciones = opciones.Value;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        log.LogInformation(
            "Consumidor de la outbox iniciado: lotes de {Lote}, cada {Intervalo}s con la cola vacia.",
            _opciones.TamanoLoteOutbox, _opciones.IntervaloOutboxSegundos);

        while (!ct.IsCancellationRequested)
        {
            int leidos;

            try
            {
                leidos = await ProcesarLoteAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Cayo el lote entero, tipicamente la base. Se espera mas que de costumbre antes de
                // volver, para no insistir sobre un recurso que ya esta en problemas.
                log.LogError(ex, "Fallo el lote de la outbox. Se reintenta en {Pausa}s.",
                    _opciones.PausaTrasErrorSegundos);

                await EsperaSegura.DormirAsync(TimeSpan.FromSeconds(_opciones.PausaTrasErrorSegundos), ct);
                continue;
            }

            await Latido.RegistrarAsync(
                ambitos, log, ServiciosVigilados.ConsumidorOutbox, Tolerancia(),
                $"{leidos} evento(s) en el ultimo lote.", ct);

            // Un lote lleno significa cola acumulada: se sigue de inmediato en vez de dormir.
            if (leidos < _opciones.TamanoLoteOutbox)
                await EsperaSegura.DormirAsync(TimeSpan.FromSeconds(_opciones.IntervaloOutboxSegundos), ct);
        }

        log.LogInformation("Consumidor de la outbox detenido.");
    }

    /// <summary>
    /// Cuanto puede pasar sin latido antes de darlo por detenido. Tres ciclos de holgura: uno
    /// perdido puede ser un lote lento, tres seguidos ya no.
    /// </summary>
    private TimeSpan Tolerancia() =>
        TimeSpan.FromSeconds(Math.Max(_opciones.IntervaloOutboxSegundos, _opciones.PausaTrasErrorSegundos) * 3);

    /// <summary>Devuelve cuantos eventos traia el lote, no cuantos salieron bien.</summary>
    private async Task<int> ProcesarLoteAsync(CancellationToken ct)
    {
        IReadOnlyList<EventoSistema> pendientes;
        int reintentosMaximos;

        // Ambito corto y aparte solo para leer: los eventos vienen desprendidos y cada uno se
        // procesa despues con su propio DbContext.
        using (var ambito = ambitos.CreateScope())
        {
            pendientes = await ambito.ServiceProvider
                .GetRequiredService<IEventoSistemaService>()
                .ObtenerPendientesAsync(_opciones.TamanoLoteOutbox, ProcesadorOutbox.TiposQueAtiende, ct);

            var configuracion = await ambito.ServiceProvider
                .GetRequiredService<IConfiguracionReglasService>()
                .ObtenerTodasAsync(ct);

            reintentosMaximos = LeerReintentos(configuracion);
        }

        foreach (var evento in pendientes)
        {
            if (ct.IsCancellationRequested)
                break;

            await ProcesarUnoAsync(evento, reintentosMaximos, ct);
        }

        return pendientes.Count;
    }

    private async Task ProcesarUnoAsync(EventoSistema evento, int reintentosMaximos, CancellationToken ct)
    {
        var fallo = await IntentarAsync(evento, ct);

        if (fallo is null)
            return;

        log.LogError(fallo, "Fallo el evento {EventoId} ({Tipo}). Correlation {CorrelationId}",
            evento.EventoId, evento.Tipo, evento.CorrelationId);

        // El fallo se marca en un ambito nuevo: el DbContext del intento pudo quedar con cambios a
        // medias, y guardarlos junto con la marca de error los daria por buenos.
        using var ambito = ambitos.CreateScope();

        await ambito.ServiceProvider
            .GetRequiredService<IEventoSistemaService>()
            .MarcarFallidoAsync(evento.EventoId, fallo.ToString(), reintentosMaximos, ct);
    }

    /// <summary>Procesa el evento en su propio ambito. Devuelve la excepcion si fallo, o nulo si salio bien.</summary>
    private async Task<Exception?> IntentarAsync(EventoSistema evento, CancellationToken ct)
    {
        using var ambito = ambitos.CreateScope();

        try
        {
            await ambito.ServiceProvider
                .GetRequiredService<ProcesadorOutbox>()
                .ProcesarAsync(evento, ct);

            await ambito.ServiceProvider
                .GetRequiredService<IEventoSistemaService>()
                .MarcarProcesadoAsync(evento.EventoId, ct);

            return null;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return ex;
        }
    }

    /// <summary>
    /// El tope de intentos vive en ConfiguracionReglas (Seccion 9.6.2), ajustable sin redeploy.
    /// Un evento que los agota queda en Fallido con su ultimo error, no se pierde en silencio.
    /// </summary>
    private static int LeerReintentos(IReadOnlyDictionary<string, string> configuracion) =>
        configuracion.TryGetValue(ClavesConfiguracion.ReintentosMaximosEvento, out var valor)
        && int.TryParse(valor, out var n) && n > 0
            ? n
            : ReintentosPorDefecto;
}
