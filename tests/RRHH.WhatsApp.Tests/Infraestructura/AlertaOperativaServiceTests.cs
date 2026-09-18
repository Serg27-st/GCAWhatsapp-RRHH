using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Servicios;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// T1.13 (V32, ARQ-09): lo que una persona tiene que resolver —una plantilla sin aprobar, una vacante
/// sin formulario— se registra como alerta agrupada, no como evento de la outbox sin consumidor (M1).
/// Una plantilla sin aprobar dispara la misma alerta por cada postulante: una fila por ocurrencia
/// enterraría lo único que importa, que la plantilla falta.
/// </summary>
public class AlertaOperativaServiceTests : IDisposable
{
    private readonly RrhhDbContext _db;
    private readonly FakeTimeProvider _reloj = new(new DateTimeOffset(2026, 9, 14, 15, 0, 0, TimeSpan.Zero));
    private readonly AlertaOperativaService _alertas;

    public AlertaOperativaServiceTests()
    {
        _db = new RrhhDbContext(new DbContextOptionsBuilder<RrhhDbContext>()
            .UseInMemoryDatabase($"alertas-{Guid.NewGuid()}")
            .Options);

        _db.Database.EnsureCreated();
        _alertas = new AlertaOperativaService(_db, _reloj);
    }

    [Fact]
    public async Task Registrar_dos_veces_la_misma_alerta_abierta_deja_una_fila_con_dos_ocurrencias()
    {
        await _alertas.RegistrarAsync(TiposAlerta.PlantillaNoAprobada, "plantilla:confirmacion", "primera");
        _reloj.Advance(TimeSpan.FromMinutes(5));
        await _alertas.RegistrarAsync(TiposAlerta.PlantillaNoAprobada, "plantilla:confirmacion", "segunda");

        var alerta = Assert.Single(await _db.AlertasOperativas.AsNoTracking().ToListAsync());

        Assert.Equal(2, alerta.Ocurrencias);
        Assert.Equal("segunda", alerta.Detalle);
        Assert.Equal(TimeSpan.FromMinutes(5), alerta.FechaUltima - alerta.FechaPrimera);
    }

    [Fact]
    public async Task Claves_distintas_son_alertas_distintas()
    {
        await _alertas.RegistrarAsync(TiposAlerta.VacanteSinFormulario, "hc:1", "Operario");
        await _alertas.RegistrarAsync(TiposAlerta.VacanteSinFormulario, "hc:2", "Almacenero");

        Assert.Equal(2, (await _alertas.ListarAbiertasAsync()).Count);
    }

    [Fact]
    public async Task Una_alerta_resuelta_no_absorbe_la_siguiente_ocurrencia()
    {
        // Si el problema vuelve después de resolverlo, es un problema nuevo que alguien tiene que ver.
        await _alertas.RegistrarAsync(TiposAlerta.VacanteSinFormulario, "hc:1", "Operario");
        var abierta = Assert.Single(await _alertas.ListarAbiertasAsync());

        Assert.True(await _alertas.ResolverAsync(abierta.AlertaId, analistaId: 10));
        Assert.Empty(await _alertas.ListarAbiertasAsync());

        await _alertas.RegistrarAsync(TiposAlerta.VacanteSinFormulario, "hc:1", "Operario");

        var nueva = Assert.Single(await _alertas.ListarAbiertasAsync());
        Assert.NotEqual(abierta.AlertaId, nueva.AlertaId);
        Assert.Equal(1, nueva.Ocurrencias);
        Assert.Equal(2, await _db.AlertasOperativas.CountAsync());
    }

    [Fact]
    public async Task Resolver_una_alerta_ya_resuelta_no_hace_nada()
    {
        await _alertas.RegistrarAsync(TiposAlerta.MenuSinOpciones, "menu", "sin cuentas");
        var alerta = Assert.Single(await _alertas.ListarAbiertasAsync());

        Assert.True(await _alertas.ResolverAsync(alerta.AlertaId, 10));
        Assert.False(await _alertas.ResolverAsync(alerta.AlertaId, 11));

        Assert.Equal(10, (await _db.AlertasOperativas.AsNoTracking().SingleAsync()).ResueltaPorAnalistaId);
    }

    [Fact]
    public async Task El_detalle_se_recorta_al_largo_de_la_columna()
    {
        await _alertas.RegistrarAsync(TiposAlerta.MenuSinOpciones, "menu", new string('x', 1500));

        Assert.Equal(1000, (await _db.AlertasOperativas.AsNoTracking().SingleAsync()).Detalle!.Length);
    }

    public void Dispose() => _db.Dispose();
}

/// <summary>El índice único filtrado de la migración <c>AlertasOperativas</c>, que en memoria no existe.</summary>
public class AlertaOperativaSqlTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    [FactConSqlServer]
    public async Task Dos_alertas_abiertas_iguales_las_rechaza_la_base_pero_una_resuelta_convive()
    {
        await using var db = sql.CrearContexto();

        AlertaOperativa Nueva(DateTime? resuelta = null) => new()
        {
            Tipo = TiposAlerta.PlantillaNoAprobada,
            Clave = "plantilla:sql",
            Detalle = "prueba",
            Ocurrencias = 1,
            FechaPrimera = DateTime.UtcNow,
            FechaUltima = DateTime.UtcNow,
            FechaResuelta = resuelta
        };

        db.AlertasOperativas.Add(Nueva(resuelta: DateTime.UtcNow));
        db.AlertasOperativas.Add(Nueva());
        await db.SaveChangesAsync();

        db.AlertasOperativas.Add(Nueva());
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [FactConSqlServer]
    public async Task El_servicio_agrupa_contra_SQL_Server()
    {
        await using var db = sql.CrearContexto();
        var alertas = new AlertaOperativaService(db, TimeProvider.System);

        await alertas.RegistrarAsync(TiposAlerta.VacanteSinFormulario, "hc:sql", "uno");
        await alertas.RegistrarAsync(TiposAlerta.VacanteSinFormulario, "hc:sql", "dos");

        var fila = await db.AlertasOperativas.AsNoTracking().SingleAsync(a => a.Clave == "hc:sql");
        Assert.Equal(2, fila.Ocurrencias);
    }
}
