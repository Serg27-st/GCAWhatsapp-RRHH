using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Domain.Entidades;

namespace RRHH.WhatsApp.Worker;

/// <summary>
/// Garantiza que un solo Worker procese (V24). <c>EventosSistema</c> no tiene reserva por fila, y
/// dos instancias tomarian el mismo evento y podrian mandar dos veces el mismo WhatsApp: el patron
/// que causo el bloqueo original de la linea.
/// <para>
/// Se registra antes que los bucles. El host arranca los servicios de a uno y en orden, asi que
/// mientras esta guardia espera el candado ninguno de ellos empezo. Una segunda instancia queda en
/// espera sin tocar la cola, y toma el relevo si la activa muere.
/// </para>
/// </summary>
public sealed class GuardiaInstancia(
    ICandadoInstancia candado,
    IServiceScopeFactory ambitos,
    IHostApplicationLifetime vida,
    IOptions<OpcionesWorker> opciones,
    ILogger<GuardiaInstancia> log) : IHostedService
{
    private readonly OpcionesWorker _opciones = opciones.Value;
    private CancellationTokenSource? _vigilancia;
    private Task? _tareaVigilancia;

    /// <summary>
    /// Si se detuvo por haber perdido el candado. <c>Program</c> lo convierte en un codigo de salida
    /// distinto de cero, para que el administrador de servicios lo trate como una falla y lo reinicie.
    /// </summary>
    public bool PerdioCandado { get; private set; }

    private TimeSpan Espera => TimeSpan.FromSeconds(_opciones.EsperaCandadoSegundos);

    private TimeSpan Verificacion => TimeSpan.FromSeconds(_opciones.VerificacionCandadoSegundos);

    public async Task StartAsync(CancellationToken ct)
    {
        var avisado = false;
        bool? tomado;

        while ((tomado = await IntentarAsync(ct)) is not true)
        {
            // Se avisa una vez: una instancia de reserva puede pasar dias esperando, y repetirlo en
            // cada intento enterraria el log.
            if (tomado is false && !avisado)
            {
                log.LogWarning(
                    "Otra instancia del Worker tiene el candado. Esta queda en espera y no procesa nada " +
                    "hasta que se libere; reintenta cada {Espera}s.", _opciones.EsperaCandadoSegundos);

                avisado = true;
            }

            await Task.Delay(Espera, ct);
        }

        log.LogInformation(
            "Candado tomado: esta es la instancia activa del Worker ({Maquina}, PID {Pid}).",
            Environment.MachineName, Environment.ProcessId);

        _vigilancia = new CancellationTokenSource();
        _tareaVigilancia = VigilarAsync(_vigilancia.Token);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        // Nunca lo tomo: se apago mientras esperaba.
        if (_vigilancia is null)
            return;

        await _vigilancia.CancelAsync();

        if (_tareaVigilancia is not null)
            await _tareaVigilancia;

        // Los bucles ya se detuvieron: el host los apaga en orden inverso al de arranque, y esta
        // guardia fue la primera en arrancar. Recien ahora se puede soltar sin dejar a dos trabajando.
        await candado.LiberarAsync();

        _vigilancia.Dispose();

        log.LogInformation("Candado del Worker liberado.");
    }

    /// <summary>True si lo tomo, false si lo tiene otra instancia, nulo si no se pudo preguntar.</summary>
    private async Task<bool?> IntentarAsync(CancellationToken ct)
    {
        try
        {
            return await candado.IntentarTomarAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Tipicamente el servidor arranco antes que SQL Server. Se sigue intentando.
            log.LogError(ex, "No se pudo consultar el candado del Worker. Se reintenta en {Espera}s.",
                _opciones.EsperaCandadoSegundos);

            return null;
        }
    }

    /// <summary>
    /// Comprueba que el candado sigue siendo suyo. Si la conexion se cae, la sesion termina y el
    /// candado se suelta: otra instancia podria tomarlo, y seguir procesando seria trabajar a la par.
    /// </summary>
    private async Task VigilarAsync(CancellationToken ct)
    {
        // Tres verificaciones de holgura, como los bucles: una perdida puede ser una base lenta.
        var tolerancia = Verificacion * 3;
        var quien = $"{Environment.MachineName}, PID {Environment.ProcessId}";

        while (!ct.IsCancellationRequested)
        {
            // Dice en /health quien es la activa: con dos Workers en juego, es lo primero que se pregunta.
            await Latido.RegistrarAsync(ambitos, log, ServiciosVigilados.InstanciaActiva, tolerancia, quien, ct);

            await EsperaSegura.DormirAsync(Verificacion, ct);

            if (ct.IsCancellationRequested)
                break;

            bool sigue;

            try
            {
                sigue = await candado.SigueTomadoAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "No se pudo comprobar el candado del Worker.");
                sigue = false;
            }

            if (sigue)
                continue;

            log.LogCritical(
                "Se perdio el candado del Worker. Se detiene el proceso para no procesar a la par de otra " +
                "instancia; al reiniciarse, espera su turno.");

            PerdioCandado = true;
            vida.StopApplication();

            return;
        }
    }
}
