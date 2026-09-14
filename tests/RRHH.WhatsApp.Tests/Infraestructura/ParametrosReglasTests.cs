using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RRHH.WhatsApp.Api.Controllers;
using RRHH.WhatsApp.Api.Seguridad;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// Los parametros de las reglas se cambian desde la pantalla de configuracion. Lo que se prueba es
/// que un valor mal escrito no llegue a las reglas: estas los leen como numero o como si/no, y un
/// valor que no entienden las deja con su valor por defecto sin que nadie lo note.
/// </summary>
public class ParametrosReglasTests
{
    private readonly IConfiguracionReglasService _configuracion = Substitute.For<IConfiguracionReglasService>();
    private readonly ConfiguracionController _controlador;

    public ParametrosReglasTests()
    {
        _configuracion.ObtenerTodasAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>
            {
                ["escalamiento.horas"] = "2",
                ["escalamiento.solo_horario_laboral"] = "true",
                ["datos.version_aviso_privacidad"] = "v1"
            }));

        _controlador = new ConfiguracionController(
            Substitute.For<IPlantillaService>(),
            Substitute.For<IHorarioAtencionService>(),
            Substitute.For<IPostulacionService>(),
            _configuracion,
            NullLogger<ConfiguracionController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimsAnalista.AnalistaId, "1"), new Claim(ClaimsAnalista.Rol, "Sistemas")],
                        "prueba"))
                }
            }
        };
    }

    private Task<IActionResult> GuardarAsync(string clave, string valor) =>
        _controlador.GuardarParametro(clave, valor, default);

    /// <summary>Una clave mal escrita crearia una fila que ninguna regla lee.</summary>
    [Fact]
    public async Task Una_clave_que_no_existe_no_se_crea()
    {
        Assert.IsType<NotFoundObjectResult>(await GuardarAsync("escalamiento.hora", "3"));
        await _configuracion.DidNotReceiveWithAnyArgs().EstablecerAsync(default!, default!, default);
    }

    [Fact]
    public async Task Un_numero_tiene_que_seguir_siendo_numero()
    {
        Assert.IsType<BadRequestObjectResult>(await GuardarAsync("escalamiento.horas", "dos"));
    }

    /// <summary>0 dias de retencion purgaria todos los CVs; 0 envios por segundo apagaria el bot.</summary>
    [Fact]
    public async Task Cero_no_es_un_valor_valido()
    {
        Assert.IsType<BadRequestObjectResult>(await GuardarAsync("escalamiento.horas", "0"));
    }

    [Fact]
    public async Task Un_si_no_tiene_que_seguir_siendo_si_no()
    {
        Assert.IsType<BadRequestObjectResult>(await GuardarAsync("escalamiento.solo_horario_laboral", "quizas"));
    }

    [Fact]
    public async Task Un_texto_admite_cualquier_valor()
    {
        Assert.IsType<NoContentResult>(await GuardarAsync("datos.version_aviso_privacidad", "v2"));
    }

    [Fact]
    public async Task Un_valor_valido_se_guarda()
    {
        Assert.IsType<NoContentResult>(await GuardarAsync("escalamiento.horas", " 3 "));
        await _configuracion.Received(1).EstablecerAsync("escalamiento.horas", "3", Arg.Any<CancellationToken>());
    }
}
