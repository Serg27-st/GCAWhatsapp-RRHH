using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Servicios;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// Sección 9.6.1: autenticación de analistas.
/// <para>
/// Hasta que existió, la Regla 4 —cada analista ve solo lo suyo— era decorativa: el id viajaba
/// como parámetro y cualquiera podía pedir la bandeja de cualquiera.
/// </para>
/// </summary>
public class AutenticacionTests : IDisposable
{
    private const string Clave = "contrasena-de-prueba-larga";

    private readonly RrhhDbContext _db;
    private readonly IAutenticacionService _auth;

    public AutenticacionTests()
    {
        _db = new RrhhDbContext(new DbContextOptionsBuilder<RrhhDbContext>()
            .UseInMemoryDatabase($"auth-{Guid.NewGuid()}")
            .Options);

        _db.Database.EnsureCreated();

        _db.Analistas.AddRange(
            new Analista { AnalistaId = 1, Nombre = "Ana Torres", Email = "ana@gca.pe", Activo = true },
            new Analista { AnalistaId = 2, Nombre = "Baja", Email = "baja@gca.pe", Activo = false },
            new Analista
            {
                AnalistaId = 3, Nombre = "Soporte", Email = "sistemas@gca.pe",
                Rol = RolAnalista.Sistemas, Activo = true
            });

        _db.SaveChanges();

        _auth = new AutenticacionService(_db, TimeProvider.System, NullLogger<AutenticacionService>.Instance);
    }

    [Fact]
    public async Task Con_la_contrasena_correcta_entra_y_trae_su_rol()
    {
        await _auth.EstablecerContrasenaAsync(3, Clave);

        var r = await _auth.VerificarAsync("sistemas@gca.pe", Clave);

        Assert.True(r.Exito);
        Assert.Equal(3, r.AnalistaId);
        Assert.Equal("Sistemas", r.Rol);
    }

    [Fact]
    public async Task Con_la_contrasena_equivocada_no_entra()
    {
        await _auth.EstablecerContrasenaAsync(1, Clave);

        Assert.False((await _auth.VerificarAsync("ana@gca.pe", "otra-cosa-cualquiera")).Exito);
    }

    [Fact]
    public async Task Un_analista_sin_contrasena_no_puede_entrar()
    {
        // Un alta sin contraseña no debe dejar una cuenta abierta, sino una que aún no sirve.
        Assert.False((await _auth.VerificarAsync("ana@gca.pe", Clave)).Exito);
    }

    [Fact]
    public async Task Un_analista_inactivo_no_entra_aunque_tenga_contrasena()
    {
        await _auth.EstablecerContrasenaAsync(2, Clave);

        Assert.False((await _auth.VerificarAsync("baja@gca.pe", Clave)).Exito);
    }

    [Fact]
    public async Task El_motivo_del_rechazo_no_distingue_entre_los_casos()
    {
        // Distinguirlos le diría a quien prueba credenciales cuáles de los correos son reales.
        await _auth.EstablecerContrasenaAsync(1, Clave);

        var inexistente = await _auth.VerificarAsync("nadie@gca.pe", Clave);
        var claveMala = await _auth.VerificarAsync("ana@gca.pe", "otra-cosa-cualquiera");

        Assert.Equal(inexistente.Motivo, claveMala.Motivo);
    }

    [Fact]
    public async Task La_contrasena_no_se_guarda_en_claro()
    {
        await _auth.EstablecerContrasenaAsync(1, Clave);

        var analista = await _db.Analistas.AsNoTracking().FirstAsync(a => a.AnalistaId == 1);

        Assert.DoesNotContain(Clave, analista.HashContrasena);
        Assert.NotNull(analista.FechaContrasena);
    }

    [Fact]
    public async Task Dos_analistas_con_la_misma_contrasena_dan_hashes_distintos()
    {
        // La sal por usuario es lo que impide reconocer contraseñas repetidas si se filtra la tabla.
        await _auth.EstablecerContrasenaAsync(1, Clave);
        await _auth.EstablecerContrasenaAsync(3, Clave);

        var hashes = await _db.Analistas.AsNoTracking()
            .Where(a => a.HashContrasena != null)
            .Select(a => a.HashContrasena)
            .ToListAsync();

        Assert.Equal(2, hashes.Distinct().Count());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("corta")]
    public async Task Una_contrasena_debil_se_rechaza(string debil)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _auth.EstablecerContrasenaAsync(1, debil));
    }

    [Fact]
    public async Task El_arranque_se_cierra_en_cuanto_existe_la_primera_contrasena()
    {
        Assert.True(await _auth.SinContrasenasAsync());

        await _auth.EstablecerContrasenaAsync(1, Clave);

        Assert.False(await _auth.SinContrasenasAsync());
    }

    [Fact]
    public async Task Cambiar_la_contrasena_invalida_la_anterior()
    {
        await _auth.EstablecerContrasenaAsync(1, Clave);
        await _auth.EstablecerContrasenaAsync(1, "una-contrasena-nueva-larga");

        Assert.False((await _auth.VerificarAsync("ana@gca.pe", Clave)).Exito);
        Assert.True((await _auth.VerificarAsync("ana@gca.pe", "una-contrasena-nueva-larga")).Exito);
    }

    public void Dispose() => _db.Dispose();
}
