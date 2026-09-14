using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Worker;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// V24 — un solo Worker procesa. Se prueba la guardia con el candado sustituido: lo que importa es
/// cuando deja arrancar a los bucles y cuando detiene el proceso. El candado real, contra SQL
/// Server, se prueba en <see cref="CandadoSqlServerTests"/>.
/// </summary>
public class GuardiaInstanciaTests
{
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(5);

    private readonly ICandadoInstancia _candado = Substitute.For<ICandadoInstancia>();
    private readonly IHostApplicationLifetime _vida = Substitute.For<IHostApplicationLifetime>();
    private readonly ILatidoServicio _latidos = Substitute.For<ILatidoServicio>();
    private readonly GuardiaInstancia _guardia;

    public GuardiaInstanciaTests()
    {
        _candado.SigueTomadoAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));

        var ambitos = new ServiceCollection()
            .AddSingleton(_latidos)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        _guardia = new GuardiaInstancia(
            _candado, ambitos, _vida,
            Options.Create(new OpcionesWorker { EsperaCandadoSegundos = 1, VerificacionCandadoSegundos = 1 }),
            NullLogger<GuardiaInstancia>.Instance);
    }

    private void CandadoLibre() =>
        _candado.IntentarTomarAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));

    private bool SeDetuvoElProceso() =>
        _vida.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IHostApplicationLifetime.StopApplication));

    private int Verificaciones() =>
        _candado.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ICandadoInstancia.SigueTomadoAsync));

    private static async Task EsperarHastaAsync(Func<bool> condicion)
    {
        var fin = DateTime.UtcNow + Limite;

        while (!condicion() && DateTime.UtcNow < fin)
            await Task.Delay(50);
    }

    [Fact]
    public async Task Con_el_candado_libre_deja_arrancar_a_los_bucles()
    {
        CandadoLibre();

        await _guardia.StartAsync(default).WaitAsync(Limite);
        await _guardia.StopAsync(default);
    }

    /// <summary>El caso que motivo todo esto: un segundo Worker lanzado sin que nadie lo note.</summary>
    [Fact]
    public async Task Mientras_otra_instancia_lo_tiene_no_deja_arrancar_nada()
    {
        _candado.IntentarTomarAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));

        using var apagado = new CancellationTokenSource();
        var arranque = _guardia.StartAsync(apagado.Token);

        // Mas que la espera configurada: tiene que haber reintentado al menos una vez sin arrancar.
        await Task.Delay(1500);
        Assert.False(arranque.IsCompleted);

        await apagado.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => arranque);
    }

    [Fact]
    public async Task Toma_el_relevo_cuando_la_otra_lo_suelta()
    {
        _candado.IntentarTomarAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false), Task.FromResult(true));

        await _guardia.StartAsync(default).WaitAsync(Limite);

        await _candado.Received(2).IntentarTomarAsync(Arg.Any<CancellationToken>());
        await _guardia.StopAsync(default);
    }

    /// <summary>Un servidor que arranca antes que SQL Server no deberia dejar al Worker muerto.</summary>
    [Fact]
    public async Task Si_la_base_no_responde_al_arrancar_sigue_intentando()
    {
        _candado.IntentarTomarAsync(Arg.Any<CancellationToken>()).Returns(
            _ => throw new InvalidOperationException("La base todavia no responde."),
            _ => Task.FromResult(true));

        await _guardia.StartAsync(default).WaitAsync(Limite);
        await _guardia.StopAsync(default);
    }

    /// <summary>
    /// Sin el candado, otra instancia puede tomarlo. Seguir procesando seria trabajar a la par, que es
    /// justo lo que el candado existe para impedir.
    /// </summary>
    [Fact]
    public async Task Si_pierde_el_candado_detiene_el_proceso()
    {
        CandadoLibre();
        _candado.SigueTomadoAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));

        await _guardia.StartAsync(default);
        await EsperarHastaAsync(SeDetuvoElProceso);

        Assert.True(SeDetuvoElProceso());
        Assert.True(_guardia.PerdioCandado);
    }

    [Fact]
    public async Task Mientras_lo_tiene_sigue_y_anuncia_quien_es_la_activa()
    {
        CandadoLibre();

        await _guardia.StartAsync(default);
        await EsperarHastaAsync(() => Verificaciones() >= 1);

        Assert.False(SeDetuvoElProceso());

        await _latidos.Received().RegistrarAsync(
            ServiciosVigilados.InstanciaActiva,
            Arg.Any<TimeSpan>(),
            Arg.Is<string?>(detalle => detalle != null && detalle.Contains(Environment.MachineName)),
            Arg.Any<CancellationToken>());

        await _guardia.StopAsync(default);
    }

    [Fact]
    public async Task Al_detenerse_suelta_el_candado()
    {
        CandadoLibre();

        await _guardia.StartAsync(default);
        await _guardia.StopAsync(default);

        await _candado.Received(1).LiberarAsync();
    }
}
