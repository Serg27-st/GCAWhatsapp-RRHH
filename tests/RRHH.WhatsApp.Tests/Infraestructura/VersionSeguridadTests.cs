using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Api.Configuracion;
using RRHH.WhatsApp.Api.Controllers;
using RRHH.WhatsApp.Contracts.Seguridad;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RRHH.WhatsApp.Api.Seguridad;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Servicios;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// T5.09 (ARQ-11, M10, V34): un token vive hasta nueve horas. Sin la versión de seguridad, darle de
/// baja a alguien o restablecerle la contraseña no lo saca del sistema: su token sigue entrando hasta
/// que vence solo.
/// </summary>
public class VersionSeguridadTests : IDisposable
{
    private const int AnalistaId = 1;
    private const string Clave = "contrasena-de-prueba-larga";

    private readonly RrhhDbContext _db;
    private readonly IAutenticacionService _auth;
    private readonly IAnalistaService _analistas;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public VersionSeguridadTests()
    {
        _db = new RrhhDbContext(new DbContextOptionsBuilder<RrhhDbContext>()
            .UseInMemoryDatabase($"version-{Guid.NewGuid()}")
            .Options);

        _db.Database.EnsureCreated();

        _db.Analistas.Add(new Analista
        {
            AnalistaId = AnalistaId, Nombre = "Ana Torres", Email = "ana@gca.pe",
            Rol = RolAnalista.Analista, Activo = true
        });

        _db.SaveChanges();

        _auth = new AutenticacionService(_db, TimeProvider.System, NullLogger<AutenticacionService>.Instance);
        _analistas = ServiciosDePrueba.Analistas(_db);
    }

    private static ClaimsPrincipal Token(int version) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimsAnalista.AnalistaId, AnalistaId.ToString()),
                new Claim(ClaimsAnalista.VersionSeguridad, version.ToString())
            ],
            "prueba"));

    private Task<string?> RevisarAsync(ClaimsPrincipal usuario) =>
        new VerificadorSesion(_cache).RevisarAsync(usuario, _analistas);

    private async Task<int> VersionActualAsync() =>
        (await _db.Analistas.AsNoTracking().FirstAsync(a => a.AnalistaId == AnalistaId)).VersionSeguridad;

    [Fact]
    public async Task Un_analista_nuevo_arranca_en_la_version_uno_y_su_token_la_trae()
    {
        await _auth.EstablecerContrasenaAsync(AnalistaId, Clave);

        var resultado = await _auth.VerificarAsync("ana@gca.pe", Clave);

        Assert.True(resultado.Exito);
        Assert.Equal(await VersionActualAsync(), resultado.VersionSeguridad);
        Assert.Null(await RevisarAsync(Token(resultado.VersionSeguridad)));
    }

    /// <summary>
    /// Restablecer la contraseña es lo que se hace cuando una cuenta pudo quedar comprometida: el
    /// token anterior tiene que dejar de servir, no esperar a vencer.
    /// </summary>
    [Fact]
    public async Task Restablecer_la_contrasena_deja_sin_efecto_el_token_anterior()
    {
        await _auth.EstablecerContrasenaAsync(AnalistaId, Clave);

        var sesion = await _auth.VerificarAsync("ana@gca.pe", Clave);
        var viejo = Token(sesion.VersionSeguridad);

        Assert.Null(await RevisarAsync(viejo));

        await _auth.EstablecerContrasenaAsync(AnalistaId, "otra-contrasena-larga");

        // El estado se cachea 60 s: mientras dure, el token viejo sigue entrando. Quien cierra la
        // sesión limpia la caché, y por eso la clave es parte del contrato.
        _cache.Remove(VerificadorSesion.ClaveCache(AnalistaId));

        Assert.NotNull(await RevisarAsync(viejo));
        Assert.Equal(sesion.VersionSeguridad + 1, await VersionActualAsync());

        // Y la sesión nueva sí entra.
        var nueva = await _auth.VerificarAsync("ana@gca.pe", "otra-contrasena-larga");
        Assert.Null(await RevisarAsync(Token(nueva.VersionSeguridad)));
    }

    [Fact]
    public async Task Un_analista_dado_de_baja_no_entra_aunque_su_token_sea_valido()
    {
        await _auth.EstablecerContrasenaAsync(AnalistaId, Clave);
        var sesion = await _auth.VerificarAsync("ana@gca.pe", Clave);

        var analista = await _db.Analistas.FirstAsync(a => a.AnalistaId == AnalistaId);
        analista.Activo = false;
        await _db.SaveChangesAsync();

        _cache.Remove(VerificadorSesion.ClaveCache(AnalistaId));

        Assert.NotNull(await RevisarAsync(Token(sesion.VersionSeguridad)));
    }

    [Fact]
    public async Task Un_token_sin_version_no_entra()
    {
        var sinVersion = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimsAnalista.AnalistaId, AnalistaId.ToString())], "prueba"));

        Assert.NotNull(await RevisarAsync(sinVersion));
    }

    [Fact]
    public async Task Un_token_de_alguien_que_ya_no_existe_no_entra()
    {
        var ajeno = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimsAnalista.AnalistaId, "999"),
                new Claim(ClaimsAnalista.VersionSeguridad, "1")
            ],
            "prueba"));

        Assert.NotNull(await RevisarAsync(ajeno));
    }

    /// <summary>
    /// Consultar la base en cada petición de cada analista es caro y casi siempre da lo mismo: se
    /// cachea 60 s. La contrapartida está documentada y es lo que hace falta limpiar al cerrar sesiones.
    /// </summary>
    [Fact]
    public async Task El_estado_se_consulta_una_vez_y_queda_en_cache()
    {
        await _auth.EstablecerContrasenaAsync(AnalistaId, Clave);
        var sesion = await _auth.VerificarAsync("ana@gca.pe", Clave);

        Assert.Null(await RevisarAsync(Token(sesion.VersionSeguridad)));

        await _auth.EstablecerContrasenaAsync(AnalistaId, "otra-contrasena-larga");

        // Sin limpiar la caché, el token viejo sigue entrando hasta que expire la entrada.
        Assert.Null(await RevisarAsync(Token(sesion.VersionSeguridad)));
    }

    private SesionController Controlador(int autorId = AnalistaId) =>
        new(_auth,
            _analistas,
            new EmisorTokens(Options.Create(new OpcionesJwt { Clave = new string('k', 40) }), TimeProvider.System),
            Options.Create(new OpcionesArranque()),
            _cache,
            NullLogger<SesionController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [
                            new Claim(ClaimsAnalista.AnalistaId, autorId.ToString()),
                            new Claim(ClaimsAnalista.Rol, ClaimsAnalista.RolSistemas)
                        ],
                        "prueba"))
                }
            }
        };

    /// <summary>
    /// FUN-18: cerrar las sesiones de alguien tiene que hacer efecto ya, no en un minuto. Por eso el
    /// endpoint limpia además la caché del verificador.
    /// </summary>
    [Fact]
    public async Task Cerrar_las_sesiones_deja_afuera_al_token_de_inmediato()
    {
        await _auth.EstablecerContrasenaAsync(AnalistaId, Clave);
        var sesion = await _auth.VerificarAsync("ana@gca.pe", Clave);

        var viejo = Token(sesion.VersionSeguridad);
        Assert.Null(await RevisarAsync(viejo));

        Assert.IsType<NoContentResult>(await Controlador().CerrarSesiones(AnalistaId, default));

        Assert.NotNull(await RevisarAsync(viejo));
        Assert.Equal(sesion.VersionSeguridad + 1, await VersionActualAsync());
    }

    [Fact]
    public async Task Cerrar_las_sesiones_de_alguien_que_no_existe_da_404()
    {
        Assert.IsType<NotFoundObjectResult>(await Controlador().CerrarSesiones(999, default));
    }

    /// <summary>Restablecer la contraseña desde la Api tampoco espera al minuto de caché.</summary>
    [Fact]
    public async Task Restablecer_desde_la_Api_deja_afuera_al_token_de_inmediato()
    {
        await _auth.EstablecerContrasenaAsync(AnalistaId, Clave);
        var sesion = await _auth.VerificarAsync("ana@gca.pe", Clave);

        var viejo = Token(sesion.VersionSeguridad);
        Assert.Null(await RevisarAsync(viejo));

        var respuesta = await Controlador().Restablecer(
            AnalistaId, new PeticionRestablecerContrasena("otra-contrasena-larga"), default);

        Assert.IsType<NoContentResult>(respuesta);
        Assert.NotNull(await RevisarAsync(viejo));
    }

    public void Dispose()
    {
        _cache.Dispose();
        _db.Dispose();
    }
}
