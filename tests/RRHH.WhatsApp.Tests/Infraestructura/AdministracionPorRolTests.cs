using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RRHH.WhatsApp.Api.Controllers;
using RRHH.WhatsApp.Api.Seguridad;
using RRHH.WhatsApp.Contracts.Bandeja;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// V23 — lo que una politica por rol no puede decidir porque depende del recurso: de quien es la
/// ausencia y de que cuenta es la vacante. Se prueba el controlador con los servicios sustituidos,
/// porque lo que importa es a quien deja pasar, no lo que hace despues.
/// </summary>
public class AdministracionPorRolTests
{
    private const int AnalistaId = 10;
    private const int CompaneroId = 11;
    private const int CuentaId = 7;
    private const int HcId = 1;

    private readonly ICuentaService _cuentas = Substitute.For<ICuentaService>();
    private readonly IAusenciaService _ausencias = Substitute.For<IAusenciaService>();
    private readonly IAnalistaService _analistas = Substitute.For<IAnalistaService>();
    private readonly IPostulacionService _postulaciones = Substitute.For<IPostulacionService>();

    public AdministracionPorRolTests()
    {
        _ausencias
            .RegistrarAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(c => Task.FromResult(new Ausencia
            {
                AnalistaId = c.ArgAt<int>(0),
                FechaInicio = c.ArgAt<DateTime>(1),
                FechaFin = c.ArgAt<DateTime>(2)
            }));

        _cuentas
            .ObtenerVacanteAsync(HcId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Hc?>(new Hc { HcId = HcId, CuentaId = CuentaId, Titulo = "Operario" }));
    }

    private void TrabajaLaCuenta(NivelAcceso nivel) =>
        _cuentas
            .ObtenerAccesoAsync(CuentaId, AnalistaId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(nivel));

    private static ControllerContext Como(string rol) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimsAnalista.AnalistaId, AnalistaId.ToString()),
                new Claim(ClaimsAnalista.Rol, rol)
            ], "prueba"))
        }
    };

    private AnalistasController Analistas(string rol) =>
        new(_cuentas, _ausencias, _analistas, TimeProvider.System) { ControllerContext = Como(rol) };

    private VacantesController Vacantes(string rol) =>
        new(_cuentas, _postulaciones, NullLogger<VacantesController>.Instance) { ControllerContext = Como(rol) };

    private static PeticionAusencia Semana() =>
        new(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(8), "Vacaciones");

    private static void EsProhibido(IActionResult resultado) =>
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(resultado).StatusCode);

    [Fact]
    public async Task Un_analista_registra_su_propia_ausencia()
    {
        var resultado = await Analistas("Analista").RegistrarAusencia(AnalistaId, Semana(), default);

        Assert.IsType<OkObjectResult>(resultado);
    }

    /// <summary>Regla 14: la ausencia le desvia las conversaciones nuevas al respaldo. Un compañero no decide eso.</summary>
    [Fact]
    public async Task No_registra_la_de_un_companero()
    {
        var resultado = await Analistas("Analista").RegistrarAusencia(CompaneroId, Semana(), default);

        EsProhibido(resultado);
        await _ausencias.DidNotReceiveWithAnyArgs().RegistrarAsync(default, default, default, default, default);
    }

    /// <summary>"El analista (o su jefe)".</summary>
    [Theory]
    [InlineData("Jefatura")]
    [InlineData("Sistemas")]
    public async Task Jefatura_y_Sistemas_registran_la_de_cualquiera(string rol)
    {
        var resultado = await Analistas(rol).RegistrarAusencia(CompaneroId, Semana(), default);

        Assert.IsType<OkObjectResult>(resultado);
    }

    [Fact]
    public async Task Quien_trabaja_la_cuenta_cierra_su_vacante()
    {
        TrabajaLaCuenta(NivelAcceso.Total);

        var resultado = await Vacantes("Analista").Cerrar(HcId, default);

        Assert.IsType<NoContentResult>(resultado);
        await _cuentas.Received(1).CerrarVacanteAsync(HcId, AnalistaId, Arg.Any<CancellationToken>());
    }

    /// <summary>Regla 20: cerrar una vacante apaga su formulario. No lo hace alguien de otra cuenta.</summary>
    [Fact]
    public async Task Un_analista_de_otra_cuenta_no_la_cierra()
    {
        TrabajaLaCuenta(NivelAcceso.Ninguno);

        var resultado = await Vacantes("Analista").Cerrar(HcId, default);

        EsProhibido(resultado);
        await _cuentas.DidNotReceiveWithAnyArgs().CerrarVacanteAsync(default, default, default);
    }

    [Fact]
    public async Task Sistemas_la_cierra_como_soporte()
    {
        var resultado = await Vacantes("Sistemas").Cerrar(HcId, default);

        Assert.IsType<NoContentResult>(resultado);
    }

    /// <summary>Jefatura decide la cobertura, no maneja las vacantes de cuentas que no trabaja.</summary>
    [Fact]
    public async Task Jefatura_no_maneja_vacantes_ajenas()
    {
        TrabajaLaCuenta(NivelAcceso.Ninguno);

        var resultado = await Vacantes("Jefatura").Cerrar(HcId, default);

        EsProhibido(resultado);
    }

    [Fact]
    public async Task Tampoco_crea_vacantes_en_una_cuenta_ajena()
    {
        TrabajaLaCuenta(NivelAcceso.Ninguno);

        var resultado = await Vacantes("Analista").Crear(new PeticionCrearVacante(CuentaId, "Operario", null), default);

        EsProhibido(resultado);
        await _cuentas.DidNotReceiveWithAnyArgs().CrearVacanteAsync(default, default!, default, default, default);
    }

    [Fact]
    public async Task Una_vacante_que_no_existe_da_404()
    {
        var resultado = await Vacantes("Sistemas").Cerrar(999, default);

        Assert.IsType<NotFoundObjectResult>(resultado);
    }

    [Fact]
    public async Task No_ve_las_ausencias_de_un_companero()
    {
        EsProhibido(await Analistas("Analista").Ausencias(CompaneroId, default));
    }

    [Fact]
    public async Task No_cancela_la_ausencia_de_un_companero()
    {
        var resultado = await Analistas("Analista").CancelarAusencia(CompaneroId, 5, default);

        EsProhibido(resultado);
        await _ausencias.DidNotReceiveWithAnyArgs().EliminarAsync(default, default, default);
    }

    [Fact]
    public async Task Cancela_la_propia()
    {
        _ausencias.EliminarAsync(AnalistaId, 5, Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));

        Assert.IsType<NoContentResult>(await Analistas("Analista").CancelarAusencia(AnalistaId, 5, default));
    }

    /// <summary>La ausencia 5 es de otro: cambiar el numero en la ruta propia no sirve para borrarla.</summary>
    [Fact]
    public async Task Una_ausencia_ajena_no_se_cancela_desde_la_ruta_propia()
    {
        Assert.IsType<NotFoundObjectResult>(await Analistas("Analista").CancelarAusencia(AnalistaId, 5, default));
    }
}
