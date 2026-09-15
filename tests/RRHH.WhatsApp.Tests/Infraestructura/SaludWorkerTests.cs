using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RRHH.WhatsApp.Api.Salud;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Servicios;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// Sección 9.6.2: detectar que el Worker dejó de procesar.
/// <para>
/// Antes de esto <c>/health</c> devolvía Healthy con el Worker muerto, y las reglas por tiempo
/// —escalamiento de 2h, recordatorio de 24h, aviso de 48h, archivado de 90 días— dejaban de correr
/// sin que nadie se enterara.
/// </para>
/// </summary>
public class SaludWorkerTests : IDisposable
{
    private readonly RrhhDbContext _db;
    private readonly ILatidoServicio _latidos;
    private readonly ChequeoWorker _chequeo;

    public SaludWorkerTests()
    {
        _db = new RrhhDbContext(new DbContextOptionsBuilder<RrhhDbContext>()
            .UseInMemoryDatabase($"salud-{Guid.NewGuid()}")
            .Options);

        _db.Database.EnsureCreated();

        _latidos = new LatidoServicioService(_db, TimeProvider.System);
        _chequeo = new ChequeoWorker(_latidos);
    }

    private Task<HealthCheckResult> ChequearAsync() =>
        _chequeo.CheckHealthAsync(new HealthCheckContext());

    /// <summary>Deja latiendo a los tres bucles, con el desfase indicado.</summary>
    private async Task LatirTodosAsync(TimeSpan tolerancia, TimeSpan? hace = null)
    {
        foreach (var servicio in ServiciosVigilados.Todos)
            await _latidos.RegistrarAsync(servicio, tolerancia, "ok");

        if (hace is not { } atraso)
            return;

        foreach (var latido in await _db.LatidosServicio.ToListAsync())
            latido.FechaUtc = DateTime.UtcNow - atraso;

        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Sin_ningun_latido_el_Worker_se_reporta_detenido()
    {
        // Es el estado de una base recien migrada, o de un Worker que nunca arranco.
        var resultado = await ChequearAsync();

        Assert.Equal(HealthStatus.Unhealthy, resultado.Status);
        Assert.Contains("sin latido", resultado.Description);
    }

    [Fact]
    public async Task Con_los_tres_bucles_latiendo_esta_sano()
    {
        await LatirTodosAsync(TimeSpan.FromMinutes(15));

        var resultado = await ChequearAsync();

        Assert.Equal(HealthStatus.Healthy, resultado.Status);
        Assert.Equal(ServiciosVigilados.Todos.Count, resultado.Data.Count);
    }

    [Fact]
    public async Task Un_latido_mas_viejo_que_su_tolerancia_pone_el_health_en_rojo()
    {
        await LatirTodosAsync(TimeSpan.FromMinutes(15), hace: TimeSpan.FromMinutes(40));

        var resultado = await ChequearAsync();

        Assert.Equal(HealthStatus.Unhealthy, resultado.Status);
        Assert.Contains("sin latir", resultado.Description);
    }

    [Fact]
    public async Task Dentro_de_la_tolerancia_un_latido_viejo_no_alarma()
    {
        // La holgura existe para que un ciclo lento no dispare una falsa alarma.
        await LatirTodosAsync(TimeSpan.FromMinutes(15), hace: TimeSpan.FromMinutes(10));

        Assert.Equal(HealthStatus.Healthy, (await ChequearAsync()).Status);
    }

    [Fact]
    public async Task Basta_con_que_un_solo_bucle_se_detenga()
    {
        // La purga corre cada 24h y el barrido cada 5 minutos: un umbral global daria falsas
        // alarmas en uno o silencio en el otro. Por eso cada bucle declara su propia tolerancia.
        await LatirTodosAsync(TimeSpan.FromMinutes(15));

        var barrido = await _db.LatidosServicio
            .FirstAsync(l => l.Servicio == ServiciosVigilados.BarridoTiempo);

        barrido.FechaUtc = DateTime.UtcNow.AddHours(-2);
        await _db.SaveChangesAsync();

        var resultado = await ChequearAsync();

        Assert.Equal(HealthStatus.Unhealthy, resultado.Status);
        Assert.Contains(ServiciosVigilados.BarridoTiempo, resultado.Description);
    }

    [Fact]
    public async Task El_latido_se_actualiza_en_vez_de_acumular_filas()
    {
        // Interesa el ultimo latido, no el historial: una fila por bucle.
        await _latidos.RegistrarAsync(ServiciosVigilados.PurgaCv, TimeSpan.FromHours(72), "primero");
        await _latidos.RegistrarAsync(ServiciosVigilados.PurgaCv, TimeSpan.FromHours(72), "segundo");

        var latido = Assert.Single(await _latidos.ListarAsync());

        Assert.Equal("segundo", latido.Detalle);
    }

    [Fact]
    public async Task El_detalle_del_ultimo_ciclo_llega_al_health()
    {
        // Distingue "vivo" de "vivo y trabajando": sirve para diagnosticar sin entrar al servidor.
        foreach (var servicio in ServiciosVigilados.Todos)
            await _latidos.RegistrarAsync(servicio, TimeSpan.FromMinutes(15), "3 evento(s)");

        var resultado = await ChequearAsync();

        Assert.Equal(HealthStatus.Healthy, resultado.Status);
        Assert.Contains(ServiciosVigilados.ConsumidorOutbox, resultado.Data.Keys);
    }

    public void Dispose() => _db.Dispose();
}
