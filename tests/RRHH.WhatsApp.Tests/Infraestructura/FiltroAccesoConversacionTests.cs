using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NSubstitute;
using RRHH.WhatsApp.Api.Controllers;
using RRHH.WhatsApp.Api.Seguridad;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// Regla 4 en la frontera HTTP. El filtro es lo unico que separa una conversacion ajena de quien
/// conoce su id, asi que se prueba solo, sin base: lo que importa es que deja pasar y que no.
/// </summary>
public class FiltroAccesoConversacionTests
{
    private const int AnalistaId = 10;
    private const int ConversacionId = 5;

    private readonly IConversacionService _conversaciones = Substitute.For<IConversacionService>();

    private async Task<(bool Paso, IActionResult? Resultado)> EjecutarAsync(
        string metodo, NivelAcceso nivel, bool conId = true, bool permiteTomar = false)
    {
        _conversaciones
            .ObtenerAccesoAsync(ConversacionId, AnalistaId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(nivel));

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimsAnalista.AnalistaId, AnalistaId.ToString())], "prueba"))
        };

        http.Request.Method = metodo;

        var accion = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var argumentos = new Dictionary<string, object?>();

        if (conId)
            argumentos["id"] = ConversacionId;

        // El atributo [PermiteTomar] viaja en los metadatos del endpoint, como en el enrutado real.
        if (permiteTomar)
            accion.ActionDescriptor.EndpointMetadata = [new PermiteTomarAttribute()];

        var contexto = new ActionExecutingContext(accion, [], argumentos, controller: new object());
        var paso = false;

        await new FiltroAccesoConversacion(_conversaciones).OnActionExecutionAsync(contexto, () =>
        {
            paso = true;
            return Task.FromResult(new ActionExecutedContext(accion, [], new object()));
        });

        return (paso, contexto.Result);
    }

    [Fact]
    public async Task Responder_una_conversacion_ajena_da_404_y_no_llega_a_la_accion()
    {
        var (paso, resultado) = await EjecutarAsync("POST", NivelAcceso.Ninguno);

        Assert.False(paso);
        Assert.IsType<NotFoundObjectResult>(resultado);
    }

    /// <summary>404 y no 403: un 403 le confirmaria a quien prueba ids que la conversacion existe.</summary>
    [Fact]
    public async Task Tampoco_se_puede_mirar()
    {
        var (paso, resultado) = await EjecutarAsync("GET", NivelAcceso.Ninguno);

        Assert.False(paso);
        Assert.IsType<NotFoundObjectResult>(resultado);
    }

    [Fact]
    public async Task Sistemas_puede_mirar()
    {
        var (paso, resultado) = await EjecutarAsync("GET", NivelAcceso.Lectura);

        Assert.True(paso);
        Assert.Null(resultado);
    }

    [Fact]
    public async Task Sistemas_no_puede_actuar_sobre_lo_ajeno()
    {
        var (paso, resultado) = await EjecutarAsync("POST", NivelAcceso.Lectura);

        Assert.False(paso);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(resultado).StatusCode);
    }

    [Fact]
    public async Task Quien_la_atiende_pasa()
    {
        var (paso, _) = await EjecutarAsync("POST", NivelAcceso.Total);

        Assert.True(paso);
    }

    /// <summary>
    /// FUN-01: tomar corre con nivel Lectura porque el hilo todavia no es de quien lo toma. Es la
    /// unica excepcion, y por eso va marcada en la accion y no abierta para todo el controlador.
    /// </summary>
    [Fact]
    public async Task Tomar_pasa_con_nivel_de_lectura()
    {
        var (paso, resultado) = await EjecutarAsync("POST", NivelAcceso.Lectura, permiteTomar: true);

        Assert.True(paso);
        Assert.Null(resultado);
    }

    [Fact]
    public async Task Tomar_sigue_sin_pasar_si_la_conversacion_no_se_ve()
    {
        var (paso, resultado) = await EjecutarAsync("POST", NivelAcceso.Ninguno, permiteTomar: true);

        Assert.False(paso);
        Assert.IsType<NotFoundObjectResult>(resultado);
    }

    /// <summary>La excepcion es de «tomar»: cualquier otra accion con nivel Lectura sigue rechazada.</summary>
    [Fact]
    public void Solo_la_accion_de_tomar_lleva_el_atributo()
    {
        var conAtributo = typeof(ConversacionesController)
            .GetMethods()
            .Where(m => m.GetCustomAttributes(typeof(PermiteTomarAttribute), inherit: true).Length > 0)
            .Select(m => m.Name)
            .ToList();

        Assert.Equal(["Tomar"], conAtributo);
    }

    [Fact]
    public async Task Las_listas_no_llevan_id_y_no_se_consultan()
    {
        var (paso, _) = await EjecutarAsync("GET", NivelAcceso.Ninguno, conId: false);

        Assert.True(paso);
        await _conversaciones.DidNotReceiveWithAnyArgs().ObtenerAccesoAsync(default, default, default);
    }

    /// <summary>
    /// La proteccion vive en el atributo del controlador. Si alguien lo quita, cada accion con id
    /// queda abierta a cualquier analista con sesion, y nada mas lo notaria.
    /// </summary>
    [Fact]
    public void El_controlador_de_conversaciones_lleva_el_filtro()
    {
        var filtros = typeof(ConversacionesController)
            .GetCustomAttributes(typeof(TypeFilterAttribute), inherit: true)
            .Cast<TypeFilterAttribute>();

        Assert.Contains(filtros, f => f.ImplementationType == typeof(FiltroAccesoConversacion));
    }
}
