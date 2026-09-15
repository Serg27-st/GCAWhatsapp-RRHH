using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Proveedores;

namespace RRHH.WhatsApp.Tests.Proveedores;

/// <summary>
/// COR-12/AL8 (T0.04): <see cref="ProveedorParametrosEnvio"/> es lo que hace que
/// <c>envio.maximo_por_segundo</c> deje de ser un numero leido una vez al arrancar. Se prueba con
/// un <see cref="IConfiguracionReglasService"/> falso (para contar lecturas) y un
/// <see cref="TimeProvider"/> escrito a mano —el paquete
/// <c>Microsoft.Extensions.TimeProvider.Testing</c> llega recien en T0.10—.
/// </summary>
public class ProveedorParametrosEnvioTests
{
    /// <summary>Reloj de mano: solo lo que esta prueba necesita, avanzar el tiempo a voluntad.</summary>
    private sealed class RelojFalso : TimeProvider
    {
        private DateTimeOffset _ahora = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _ahora;

        public void Avanzar(TimeSpan transcurrido) => _ahora += transcurrido;
    }

    private sealed class ConfiguracionReglasFalsa : IConfiguracionReglasService
    {
        public int Llamadas;
        public readonly Dictionary<string, string> Valores = new();
        public Exception? ExcepcionAlLeer;

        public Task<IReadOnlyDictionary<string, string>> ObtenerTodasAsync(CancellationToken ct = default)
        {
            Llamadas++;

            if (ExcepcionAlLeer is { } excepcion)
                throw excepcion;

            return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(Valores));
        }

        public Task<IReadOnlyList<ConfiguracionRegla>> ListarAsync(CancellationToken ct = default) =>
            throw new NotSupportedException("No lo usa ProveedorParametrosEnvio.");

        public Task EstablecerAsync(string clave, string valor, CancellationToken ct = default) =>
            throw new NotSupportedException("No lo usa ProveedorParametrosEnvio.");
    }

    private static (ProveedorParametrosEnvio Proveedor, ConfiguracionReglasFalsa Config, RelojFalso Reloj) Construir(
        int fallback = 10)
    {
        var configuracion = new ConfiguracionReglasFalsa();
        var reloj = new RelojFalso();

        var servicios = new ServiceCollection();
        servicios.AddSingleton<IConfiguracionReglasService>(configuracion);
        var proveedorServicios = servicios.BuildServiceProvider();

        var proveedor = new ProveedorParametrosEnvio(
            proveedorServicios.GetRequiredService<IServiceScopeFactory>(),
            fallback,
            reloj,
            NullLogger<ProveedorParametrosEnvio>.Instance);

        return (proveedor, configuracion, reloj);
    }

    [Fact]
    public async Task Lee_el_valor_sembrado_en_la_base()
    {
        var (proveedor, config, _) = Construir();
        config.Valores["envio.maximo_por_segundo"] = "7";

        Assert.Equal(7, await proveedor.ObtenerMaximoPorSegundoAsync(default));
        Assert.Equal(1, config.Llamadas);
    }

    [Fact]
    public async Task Dentro_de_los_30_segundos_no_vuelve_a_consultar_aunque_cambie_el_valor()
    {
        var (proveedor, config, reloj) = Construir();
        config.Valores["envio.maximo_por_segundo"] = "7";

        Assert.Equal(7, await proveedor.ObtenerMaximoPorSegundoAsync(default));

        config.Valores["envio.maximo_por_segundo"] = "3";
        reloj.Avanzar(TimeSpan.FromSeconds(29));

        Assert.Equal(7, await proveedor.ObtenerMaximoPorSegundoAsync(default));
        Assert.Equal(1, config.Llamadas);
    }

    [Fact]
    public async Task Pasados_30_segundos_recarga_y_devuelve_el_valor_nuevo()
    {
        var (proveedor, config, reloj) = Construir();
        config.Valores["envio.maximo_por_segundo"] = "7";
        Assert.Equal(7, await proveedor.ObtenerMaximoPorSegundoAsync(default));

        config.Valores["envio.maximo_por_segundo"] = "3";
        reloj.Avanzar(TimeSpan.FromSeconds(30));

        Assert.Equal(3, await proveedor.ObtenerMaximoPorSegundoAsync(default));
        Assert.Equal(2, config.Llamadas);
    }

    [Fact]
    public async Task Clave_ausente_usa_el_fallback_de_appsettings()
    {
        var (proveedor, _, _) = Construir(fallback: 12);

        Assert.Equal(12, await proveedor.ObtenerMaximoPorSegundoAsync(default));
    }

    [Fact]
    public async Task Clave_no_numerica_usa_el_fallback()
    {
        var (proveedor, config, _) = Construir(fallback: 12);
        config.Valores["envio.maximo_por_segundo"] = "diez";

        Assert.Equal(12, await proveedor.ObtenerMaximoPorSegundoAsync(default));
    }

    [Fact]
    public async Task Clave_no_positiva_usa_el_fallback()
    {
        var (proveedor, config, _) = Construir(fallback: 12);
        config.Valores["envio.maximo_por_segundo"] = "0";

        Assert.Equal(12, await proveedor.ObtenerMaximoPorSegundoAsync(default));
    }

    [Fact]
    public async Task Si_la_base_falla_conserva_el_ultimo_valor_conocido_sin_propagar()
    {
        var (proveedor, config, reloj) = Construir(fallback: 10);
        config.Valores["envio.maximo_por_segundo"] = "7";
        Assert.Equal(7, await proveedor.ObtenerMaximoPorSegundoAsync(default));

        reloj.Avanzar(TimeSpan.FromSeconds(30));
        config.ExcepcionAlLeer = new InvalidOperationException("SQL Server no responde");

        var valor = await proveedor.ObtenerMaximoPorSegundoAsync(default);

        Assert.Equal(7, valor); // Ultimo valor conocido: ni el fallback ni una excepcion.
    }

    [Fact]
    public async Task Si_la_base_falla_en_la_primera_lectura_usa_el_fallback()
    {
        var (proveedor, config, _) = Construir(fallback: 9);
        config.ExcepcionAlLeer = new InvalidOperationException("SQL Server no responde");

        var valor = await proveedor.ObtenerMaximoPorSegundoAsync(default);

        Assert.Equal(9, valor);
    }

    [Fact]
    public async Task Una_falla_no_hace_reintentar_la_base_dentro_de_la_ventana_de_cache()
    {
        // La cache se renueva igual aunque la lectura falle, para no martillar una base caida.
        var (proveedor, config, reloj) = Construir(fallback: 10);
        config.ExcepcionAlLeer = new InvalidOperationException("SQL Server no responde");

        await proveedor.ObtenerMaximoPorSegundoAsync(default);
        reloj.Avanzar(TimeSpan.FromSeconds(1));
        await proveedor.ObtenerMaximoPorSegundoAsync(default);

        Assert.Equal(1, config.Llamadas);
    }

    [Fact]
    public async Task Criterio_de_hecho_04_el_limitador_respeta_el_nuevo_tope_tras_expirar_la_cache()
    {
        // T0.04, criterio de "hecho" de 04-documento-continuidad.md: una prueba cambia el
        // parametro y el limitador respeta el nuevo tope tras expirar la cache (tope 5 -> cambia
        // a 2 -> avanza el reloj -> en una ventana solo pasan 2 sin esperar).
        var (proveedor, config, reloj) = Construir();
        config.Valores["envio.maximo_por_segundo"] = "5";

        using var limitador = new LimitadorEnvio(ct => proveedor.ObtenerMaximoPorSegundoAsync(ct));

        // Fuerza la primera lectura (tope 5) sin gastarla en envios, para que la ventana del
        // limitador arranque vacia cuando se mida el tope nuevo.
        await proveedor.ObtenerMaximoPorSegundoAsync(default);

        config.Valores["envio.maximo_por_segundo"] = "2";
        reloj.Avanzar(TimeSpan.FromSeconds(30));

        var reloj2 = Stopwatch.StartNew();
        await limitador.EsperarTurnoAsync();
        await limitador.EsperarTurnoAsync();
        reloj2.Stop();
        Assert.True(reloj2.ElapsedMilliseconds < 200,
            $"Los primeros 2 no deberian esperar: el tope ya es 2, pero tardaron {reloj2.ElapsedMilliseconds} ms.");

        reloj2.Restart();
        await limitador.EsperarTurnoAsync();
        reloj2.Stop();
        Assert.True(reloj2.ElapsedMilliseconds >= 900,
            $"El tercer envio debio esperar: el tope efectivo es 2, pero paso en {reloj2.ElapsedMilliseconds} ms.");
    }
}
