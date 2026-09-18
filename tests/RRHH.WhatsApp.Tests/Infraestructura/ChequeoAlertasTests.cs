using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RRHH.WhatsApp.Api.Salud;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Servicios;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// T1.14 (V32): una plantilla sin aprobar o una vacante sin formulario dejan postulantes sin respuesta.
/// No tumban el sistema —por eso Degraded y no Unhealthy—, pero el monitoreo tiene que verlas.
/// </summary>
public class ChequeoAlertasTests : IDisposable
{
    private readonly RrhhDbContext _db;
    private readonly AlertaOperativaService _alertas;

    public ChequeoAlertasTests()
    {
        _db = new RrhhDbContext(new DbContextOptionsBuilder<RrhhDbContext>()
            .UseInMemoryDatabase($"chequeo-alertas-{Guid.NewGuid()}")
            .Options);

        _db.Database.EnsureCreated();
        _alertas = new AlertaOperativaService(_db, TimeProvider.System);
    }

    private Task<HealthCheckResult> ChequearAsync() =>
        new ChequeoAlertas(_alertas).CheckHealthAsync(new HealthCheckContext());

    [Fact]
    public async Task Sin_alertas_esta_sano()
    {
        Assert.Equal(HealthStatus.Healthy, (await ChequearAsync()).Status);
    }

    [Theory]
    [InlineData(TiposAlerta.PlantillaNoAprobada, "plantilla:recordatorio_24h")]
    [InlineData(TiposAlerta.VacanteSinFormulario, "hc:12")]
    public async Task Una_alerta_que_deja_postulantes_sin_respuesta_lo_degrada(string tipo, string clave)
    {
        await _alertas.RegistrarAsync(tipo, clave, "prueba");

        var resultado = await ChequearAsync();

        Assert.Equal(HealthStatus.Degraded, resultado.Status);
        Assert.Contains(clave, resultado.Description);
    }

    [Fact]
    public async Task Una_cuenta_sin_respaldo_no_degrada_el_health()
    {
        // Se ve en el panel de alertas, pero ningún postulante queda sin respuesta.
        await _alertas.RegistrarAsync(TiposAlerta.CuentaSinRespaldo, "cuenta:7", "prueba");

        Assert.Equal(HealthStatus.Healthy, (await ChequearAsync()).Status);
    }

    public void Dispose() => _db.Dispose();
}
