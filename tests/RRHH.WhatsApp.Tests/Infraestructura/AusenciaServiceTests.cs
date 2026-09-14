using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Servicios;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// Regla 14 — lo que la pantalla necesita del servicio: ver las ausencias que todavia importan y
/// poder deshacer una cargada por error.
/// </summary>
public class AusenciaServiceTests : IDisposable
{
    private const int AnalistaId = 10;
    private const int CompaneroId = 11;

    private readonly RrhhDbContext _db;
    private readonly AusenciaService _ausencias;

    public AusenciaServiceTests()
    {
        _db = new RrhhDbContext(new DbContextOptionsBuilder<RrhhDbContext>()
            .UseInMemoryDatabase($"ausencias-{Guid.NewGuid()}")
            .Options);

        _db.Database.EnsureCreated();
        _ausencias = new AusenciaService(_db);
    }

    private Task<Ausencia> RegistrarAsync(int analistaId, int desdeDias, int hastaDias) =>
        _ausencias.RegistrarAsync(
            analistaId, DateTime.UtcNow.AddDays(desdeDias), DateTime.UtcNow.AddDays(hastaDias), null);

    /// <summary>Las pasadas ya no cambian el enrutamiento: mostrarlas solo haria ruido.</summary>
    [Fact]
    public async Task Lista_las_en_curso_y_las_programadas_pero_no_las_pasadas()
    {
        await RegistrarAsync(AnalistaId, -20, -10);
        var enCurso = await RegistrarAsync(AnalistaId, -1, 2);
        var programada = await RegistrarAsync(AnalistaId, 5, 9);
        await RegistrarAsync(CompaneroId, -1, 2);

        var vigentes = await _ausencias.ListarVigentesAsync(AnalistaId, DateTime.UtcNow);

        Assert.Equal(new[] { enCurso.AusenciaId, programada.AusenciaId }, vigentes.Select(a => a.AusenciaId));
    }

    [Fact]
    public async Task Cancelarla_devuelve_al_analista_al_enrutamiento()
    {
        var ausencia = await RegistrarAsync(AnalistaId, -1, 2);

        Assert.True(await _ausencias.EliminarAsync(AnalistaId, ausencia.AusenciaId));
        Assert.False(await _ausencias.EstaAusenteAsync(AnalistaId, DateTime.UtcNow));
    }

    [Fact]
    public async Task No_se_cancela_con_el_id_de_otro_analista()
    {
        var ausencia = await RegistrarAsync(AnalistaId, -1, 2);

        Assert.False(await _ausencias.EliminarAsync(CompaneroId, ausencia.AusenciaId));
        Assert.True(await _ausencias.EstaAusenteAsync(AnalistaId, DateTime.UtcNow));
    }

    public void Dispose() => _db.Dispose();
}
