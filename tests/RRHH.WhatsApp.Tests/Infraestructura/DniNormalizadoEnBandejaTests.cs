using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RRHH.WhatsApp.Api.Controllers;
using RRHH.WhatsApp.Api.Seguridad;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// T0.07a: desde T0.07 el JobForms guarda el DNI normalizado (<see cref="DocumentoIdentidad"/>).
/// Si la bandeja busca con lo que escribió el analista tal cual, «12.345.678» no encuentra a la
/// persona que el formulario guardó como «12345678»: para el buscador (Sección 7), el historial
/// (Sección 6.1) y la anonimización (Regla 17) sería alguien que no existe.
/// </summary>
public class DniNormalizadoEnBandejaTests
{
    private const int AnalistaId = 10;
    private const string Guardado = "12345678";
    private const string ComoLoEscribe = " 12.345.678 ";

    private readonly IPostulanteService _postulantes = Substitute.For<IPostulanteService>();
    private readonly ICuentaService _cuentas = Substitute.For<ICuentaService>();
    private readonly IConversacionService _conversaciones = Substitute.For<IConversacionService>();

    private static ControllerContext ComoSistemas() => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimsAnalista.AnalistaId, AnalistaId.ToString()),
                new Claim(ClaimsAnalista.Rol, ClaimsAnalista.RolSistemas)
            ], "prueba"))
        }
    };

    private PostulantesController Postulantes() =>
        new(_postulantes, _cuentas, NullLogger<PostulantesController>.Instance) { ControllerContext = ComoSistemas() };

    // Buscar solo usa el servicio de conversaciones; el resto de las dependencias no participa.
    private ConversacionesController Conversaciones() =>
        new(_conversaciones, null!, null!, null!, null!, NullLogger<ConversacionesController>.Instance)
        {
            ControllerContext = ComoSistemas()
        };

    [Fact]
    public async Task El_buscador_encuentra_el_dni_escrito_con_separadores()
    {
        _conversaciones.BuscarPorDniAsync(Guardado, AnalistaId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Conversacion>>([]));

        var respuesta = await Conversaciones().Buscar(ComoLoEscribe, default);

        Assert.IsType<OkObjectResult>(respuesta);
        await _conversaciones.Received(1).BuscarPorDniAsync(Guardado, AnalistaId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task El_buscador_rechaza_un_dni_sin_formato_sin_consultar()
    {
        var respuesta = await Conversaciones().Buscar("12ab", default);

        Assert.IsType<BadRequestObjectResult>(respuesta);
        await _conversaciones.DidNotReceiveWithAnyArgs().BuscarPorDniAsync(default!, default, default);
    }

    [Fact]
    public async Task El_historial_busca_con_el_dni_normalizado()
    {
        _postulantes.BuscarPorDniAsync(Guardado, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Postulante?>(new Postulante { Dni = Guardado, NombreCompleto = "Maria" }));
        _postulantes.ObtenerHistorialAsync(Guardado, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Postulacion>>([]));

        var respuesta = await Postulantes().Historial(ComoLoEscribe, default);

        Assert.IsType<OkObjectResult>(respuesta);
    }

    [Fact]
    public async Task El_historial_rechaza_un_dni_sin_formato_sin_consultar()
    {
        var respuesta = await Postulantes().Historial("abc", default);

        Assert.IsType<BadRequestObjectResult>(respuesta);
        await _postulantes.DidNotReceiveWithAnyArgs().BuscarPorDniAsync(default!, default);
    }

    [Fact]
    public async Task La_anonimizacion_usa_el_dni_normalizado()
    {
        var respuesta = await Postulantes().Eliminar("12-345-678", null, default);

        Assert.IsType<NoContentResult>(respuesta);
        await _postulantes.Received(1).AnonimizarDatosAsync(Guardado, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task La_anonimizacion_rechaza_un_dni_sin_formato_sin_tocar_datos()
    {
        var respuesta = await Postulantes().Eliminar("abc", null, default);

        Assert.IsType<BadRequestObjectResult>(respuesta);
        await _postulantes.DidNotReceiveWithAnyArgs().AnonimizarDatosAsync(default!, default!, default);
    }
}
