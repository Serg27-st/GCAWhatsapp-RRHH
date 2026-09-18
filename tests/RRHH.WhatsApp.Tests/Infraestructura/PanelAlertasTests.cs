using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RRHH.WhatsApp.Api.Controllers;
using RRHH.WhatsApp.Api.Seguridad;
using RRHH.WhatsApp.Contracts.Administracion;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Servicios;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// T5.08 (FUN-15, M1): lo que una persona tiene que arreglar —una plantilla sin aprobar, una vacante
/// sin formulario— queda en <c>AlertasOperativas</c>, agrupado. Sin pantalla que lo muestre, esa tabla
/// es un lugar donde los problemas se guardan y nadie los ve.
/// </summary>
public class PanelAlertasTests : IDisposable
{
    private const int SistemasId = 1;

    private readonly RrhhDbContext _db;
    private readonly AlertaOperativaService _alertas;

    public PanelAlertasTests()
    {
        _db = new RrhhDbContext(new DbContextOptionsBuilder<RrhhDbContext>()
            .UseInMemoryDatabase($"alertas-{Guid.NewGuid()}")
            .Options);

        _db.Database.EnsureCreated();

        _alertas = new AlertaOperativaService(_db, TimeProvider.System);
    }

    private OperacionController Controlador() =>
        new(_alertas, NullLogger<OperacionController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [
                            new Claim(ClaimsAnalista.AnalistaId, SistemasId.ToString()),
                            new Claim(ClaimsAnalista.Rol, ClaimsAnalista.RolSistemas)
                        ],
                        "prueba"))
                }
            }
        };

    private static IReadOnlyList<AlertaOperativaResumen> Leer(IActionResult resultado) =>
        Assert.IsAssignableFrom<IReadOnlyList<AlertaOperativaResumen>>(
            Assert.IsType<OkObjectResult>(resultado).Value);

    [Fact]
    public async Task El_panel_muestra_las_alertas_abiertas_con_su_contador()
    {
        await _alertas.RegistrarAsync(TiposAlerta.PlantillaNoAprobada, "plantilla:cierre_cortesia", "Sin aprobar.");
        await _alertas.RegistrarAsync(TiposAlerta.PlantillaNoAprobada, "plantilla:cierre_cortesia", "Sin aprobar.");
        await _alertas.RegistrarAsync(TiposAlerta.VacanteSinFormulario, "hc:12", "Sin formulario.");

        var alertas = Leer(await Controlador().Listar(default));

        Assert.Equal(2, alertas.Count);

        // Agrupada: dos ocurrencias del mismo problema son una sola fila con contador (V32).
        var plantilla = Assert.Single(alertas, a => a.Clave == "plantilla:cierre_cortesia");
        Assert.Equal(TiposAlerta.PlantillaNoAprobada, plantilla.Tipo);
        Assert.Equal(2, plantilla.Ocurrencias);
        Assert.True(plantilla.FechaUltima >= plantilla.FechaPrimera);
    }

    [Fact]
    public async Task Resolver_la_saca_del_panel_y_deja_quien_fue()
    {
        await _alertas.RegistrarAsync(TiposAlerta.VacanteSinFormulario, "hc:12", "Sin formulario.");

        var alerta = Assert.Single(Leer(await Controlador().Listar(default)));

        Assert.IsType<NoContentResult>(await Controlador().Resolver(alerta.AlertaId, default));
        Assert.Empty(Leer(await Controlador().Listar(default)));

        var resuelta = await _db.AlertasOperativas.AsNoTracking().SingleAsync();
        Assert.NotNull(resuelta.FechaResuelta);
        Assert.Equal(SistemasId, resuelta.ResueltaPorAnalistaId);
    }

    /// <summary>
    /// Una alerta resuelta no absorbe la siguiente: si el problema vuelve, es una alerta nueva. Sin
    /// esto, darla por resuelta taparía para siempre el problema que vuelva a aparecer (V32).
    /// </summary>
    [Fact]
    public async Task Si_el_problema_vuelve_despues_de_resuelto_aparece_otra_alerta()
    {
        await _alertas.RegistrarAsync(TiposAlerta.VacanteSinFormulario, "hc:12", "Sin formulario.");

        var alerta = Assert.Single(Leer(await Controlador().Listar(default)));
        await Controlador().Resolver(alerta.AlertaId, default);

        await _alertas.RegistrarAsync(TiposAlerta.VacanteSinFormulario, "hc:12", "Sin formulario.");

        var nueva = Assert.Single(Leer(await Controlador().Listar(default)));

        Assert.NotEqual(alerta.AlertaId, nueva.AlertaId);
        Assert.Equal(1, nueva.Ocurrencias);
    }

    [Fact]
    public async Task Resolver_algo_que_ya_no_esta_abierto_no_es_un_error_de_sistema()
    {
        Assert.IsType<NotFoundObjectResult>(await Controlador().Resolver(404, default));
    }

    public void Dispose() => _db.Dispose();
}
