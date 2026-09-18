using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RRHH.WhatsApp.Api.Controllers;
using RRHH.WhatsApp.Api.Seguridad;
using RRHH.WhatsApp.Contracts.Seguridad;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// V25 — poner en marcha una base nueva. Antes no se podia: no habia analistas, darlos de alta
/// exigia ser Sistemas, y el arranque solo fijaba contraseñas de analistas que ya existieran.
/// </summary>
public class ArranqueTests
{
    private const string EmailSistemas = "sistemas@empresa.pe";
    private const string Contrasena = "una-clave-bien-larga";

    private readonly IAutenticacionService _autenticacion = Substitute.For<IAutenticacionService>();
    private readonly IAnalistaService _analistas = Substitute.For<IAnalistaService>();
    private readonly List<Analista> _existentes = [];

    public ArranqueTests()
    {
        _autenticacion.SinContrasenasAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));

        _analistas.ListarActivosAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<Analista>>(_existentes));

        _analistas.CrearAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<RolAnalista>(), Arg.Any<CancellationToken>())
            .Returns(c => Task.FromResult(new Analista
            {
                AnalistaId = 1,
                Nombre = c.ArgAt<string>(0),
                Email = c.ArgAt<string>(1).ToLowerInvariant(),
                Rol = c.ArgAt<RolAnalista>(2),
                Activo = true
            }));
    }

    private SesionController Controlador(string emailConfigurado = EmailSistemas) =>
        new(_autenticacion,
            _analistas,
            new EmisorTokens(Options.Create(new OpcionesJwt()), TimeProvider.System),
            Options.Create(new OpcionesArranque { EmailSistemas = emailConfigurado }),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<SesionController>.Instance);

    private void Existe(int id, string email, RolAnalista rol) =>
        _existentes.Add(new Analista { AnalistaId = id, Nombre = "Existente", Email = email, Rol = rol, Activo = true });

    private Task NoSeCreoNadieAsync() =>
        _analistas.DidNotReceiveWithAnyArgs().CrearAsync(default!, default!, default, default);

    [Fact]
    public async Task En_una_base_nueva_da_de_alta_al_de_Sistemas_configurado()
    {
        var resultado = await Controlador().Arranque(
            new PeticionArranque(" Sistemas@Empresa.pe ", Contrasena, "Soporte"), default);

        Assert.IsType<NoContentResult>(resultado);
        await _analistas.Received(1).CrearAsync("Soporte", "Sistemas@Empresa.pe", RolAnalista.Sistemas, Arg.Any<CancellationToken>());
        await _autenticacion.Received(1).EstablecerContrasenaAsync(1, Contrasena, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// La Api es alcanzable desde internet. Sin esta condicion, cualquiera que llegara primero a un
    /// despliegue recien publicado se daria de alta con visibilidad total.
    /// </summary>
    [Fact]
    public async Task Un_correo_que_no_es_el_configurado_no_crea_a_nadie()
    {
        var resultado = await Controlador().Arranque(new PeticionArranque("otro@empresa.pe", Contrasena), default);

        Assert.IsType<NotFoundObjectResult>(resultado);
        await NoSeCreoNadieAsync();
    }

    [Fact]
    public async Task Sin_correo_configurado_no_crea_a_nadie()
    {
        var resultado = await Controlador(emailConfigurado: "").Arranque(new PeticionArranque(EmailSistemas, Contrasena), default);

        Assert.IsType<NotFoundObjectResult>(resultado);
        await NoSeCreoNadieAsync();
    }

    /// <summary>
    /// El bloqueo silencioso que habia: con la primera contraseña en un analista comun la puerta se
    /// cerraba, y nadie podia restablecer las de los demas porque eso es solo de Sistemas.
    /// </summary>
    [Fact]
    public async Task La_primera_contrasena_no_puede_ser_de_un_analista_comun()
    {
        Existe(5, "ana@empresa.pe", RolAnalista.Analista);

        var resultado = await Controlador().Arranque(new PeticionArranque("ana@empresa.pe", Contrasena), default);

        Assert.IsType<UnprocessableEntityObjectResult>(resultado);
        await _autenticacion.DidNotReceiveWithAnyArgs().EstablecerContrasenaAsync(default, default!, default);
    }

    [Fact]
    public async Task Si_el_de_Sistemas_ya_existe_solo_le_fija_la_contrasena()
    {
        Existe(7, "soporte@empresa.pe", RolAnalista.Sistemas);

        var resultado = await Controlador().Arranque(new PeticionArranque("Soporte@Empresa.pe", Contrasena), default);

        Assert.IsType<NoContentResult>(resultado);
        await NoSeCreoNadieAsync();
        await _autenticacion.Received(1).EstablecerContrasenaAsync(7, Contrasena, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Con_una_contrasena_ya_configurada_la_puerta_esta_cerrada()
    {
        _autenticacion.SinContrasenasAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));

        var resultado = await Controlador().Arranque(new PeticionArranque(EmailSistemas, Contrasena), default);

        Assert.IsType<ConflictObjectResult>(resultado);
        await NoSeCreoNadieAsync();
    }

    /// <summary>La contraseña corta se rechaza con su motivo, no con un 500: se reintenta con una valida.</summary>
    [Fact]
    public async Task Una_contrasena_que_no_sirve_se_rechaza_con_su_motivo()
    {
        _autenticacion.EstablecerContrasenaAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("La contraseña necesita al menos 10 caracteres."));

        var resultado = await Controlador().Arranque(new PeticionArranque(EmailSistemas, "corta"), default);

        Assert.IsType<UnprocessableEntityObjectResult>(resultado);
    }
}
