using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Servicios;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// Alta de cuentas, analistas y su dotacion. Es lo que pone al sistema en condiciones de enrutar:
/// sin titular, la Regla 1 no tiene a quien asignarle la conversacion.
/// </summary>
public class AdministracionCuentasTests : IDisposable
{
    private readonly RrhhDbContext _db;
    private readonly ICuentaService _cuentas;
    private readonly IAnalistaService _analistas;

    public AdministracionCuentasTests()
    {
        _db = new RrhhDbContext(new DbContextOptionsBuilder<RrhhDbContext>()
            .UseInMemoryDatabase($"admin-{Guid.NewGuid()}")
            .Options);

        _db.Database.EnsureCreated();

        _cuentas = new CuentaService(_db);
        _analistas = new AnalistaService(_db);
    }

    private Task<Analista> AnalistaAsync(string nombre, string email) =>
        _analistas.CrearAsync(nombre, email, RolAnalista.Analista);

    [Fact]
    public async Task Crear_una_cuenta_la_deja_activa_y_lista_para_recibir_vacantes()
    {
        var cuenta = await _cuentas.CrearAsync("  Alicorp  ");

        Assert.Equal("Alicorp", cuenta.Nombre);
        Assert.True(cuenta.Activo);
    }

    [Fact]
    public async Task No_se_puede_repetir_el_nombre_de_una_cuenta()
    {
        // Es como el analista la reconoce en la bandeja: dos iguales no se distinguen.
        await _cuentas.CrearAsync("Alicorp");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _cuentas.CrearAsync("Alicorp"));
    }

    [Fact]
    public async Task No_se_puede_repetir_el_email_de_un_analista()
    {
        await AnalistaAsync("Ana Torres", "ana@gca.pe");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AnalistaAsync("Ana Duplicada", "ANA@gca.pe"));
    }

    [Fact]
    public async Task Asignar_titular_y_respaldo_deja_la_cuenta_lista()
    {
        var cuenta = await _cuentas.CrearAsync("Alicorp");
        var titular = await AnalistaAsync("Ana Torres", "ana@gca.pe");
        var respaldo = await AnalistaAsync("Luis Vega", "luis@gca.pe");

        await _cuentas.AsignarAnalistaAsync(cuenta.CuentaId, titular.AnalistaId, esBackup: false);
        await _cuentas.AsignarAnalistaAsync(cuenta.CuentaId, respaldo.AnalistaId, esBackup: true);

        var dotacion = await _cuentas.ListarConDotacionAsync();
        var fila = Assert.Single(dotacion);

        Assert.Equal(titular.AnalistaId, fila.Titular!.AnalistaId);
        Assert.Equal(respaldo.AnalistaId, fila.Respaldo!.AnalistaId);
    }

    [Fact]
    public async Task Asignar_un_segundo_titular_reemplaza_al_anterior()
    {
        // Regla 1: con dos titulares el enrutamiento elegiria uno de forma arbitraria, y el otro
        // nunca veria las conversaciones de su cuenta.
        var cuenta = await _cuentas.CrearAsync("Alicorp");
        var primero = await AnalistaAsync("Ana Torres", "ana@gca.pe");
        var segundo = await AnalistaAsync("Luis Vega", "luis@gca.pe");

        await _cuentas.AsignarAnalistaAsync(cuenta.CuentaId, primero.AnalistaId, esBackup: false);
        await _cuentas.AsignarAnalistaAsync(cuenta.CuentaId, segundo.AnalistaId, esBackup: false);

        var fila = Assert.Single(await _cuentas.ListarConDotacionAsync());

        Assert.Equal(segundo.AnalistaId, fila.Titular!.AnalistaId);
        Assert.Single(_db.AnalistaCuentas.Where(ac => ac.CuentaId == cuenta.CuentaId));
    }

    [Fact]
    public async Task Reasignar_al_mismo_analista_cambia_su_rol_sin_duplicarlo()
    {
        var cuenta = await _cuentas.CrearAsync("Alicorp");
        var analista = await AnalistaAsync("Ana Torres", "ana@gca.pe");

        await _cuentas.AsignarAnalistaAsync(cuenta.CuentaId, analista.AnalistaId, esBackup: false);
        await _cuentas.AsignarAnalistaAsync(cuenta.CuentaId, analista.AnalistaId, esBackup: true);

        var fila = Assert.Single(await _cuentas.ListarConDotacionAsync());

        Assert.Null(fila.Titular);
        Assert.Equal(analista.AnalistaId, fila.Respaldo!.AnalistaId);
    }

    [Fact]
    public async Task No_se_asigna_un_analista_que_no_existe()
    {
        var cuenta = await _cuentas.CrearAsync("Alicorp");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _cuentas.AsignarAnalistaAsync(cuenta.CuentaId, analistaId: 999, esBackup: false));
    }

    [Fact]
    public async Task No_se_asigna_sobre_una_cuenta_que_no_existe()
    {
        var analista = await AnalistaAsync("Ana Torres", "ana@gca.pe");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _cuentas.AsignarAnalistaAsync(cuentaId: 999, analista.AnalistaId, esBackup: false));
    }

    [Fact]
    public async Task Quitar_deja_la_cuenta_sin_ese_analista()
    {
        var cuenta = await _cuentas.CrearAsync("Alicorp");
        var analista = await AnalistaAsync("Ana Torres", "ana@gca.pe");

        await _cuentas.AsignarAnalistaAsync(cuenta.CuentaId, analista.AnalistaId, esBackup: false);
        await _cuentas.QuitarAnalistaAsync(cuenta.CuentaId, analista.AnalistaId);

        var fila = Assert.Single(await _cuentas.ListarConDotacionAsync());

        Assert.Null(fila.Titular);
    }

    [Fact]
    public async Task La_dotacion_cuenta_las_vacantes_abiertas_y_no_las_cerradas()
    {
        var cuenta = await _cuentas.CrearAsync("Alicorp");
        var analista = await AnalistaAsync("Ana Torres", "ana@gca.pe");

        await _cuentas.CrearVacanteAsync(cuenta.CuentaId, "Operario", null, analista.AnalistaId);
        var cerrada = await _cuentas.CrearVacanteAsync(cuenta.CuentaId, "Almacenero", null, analista.AnalistaId);

        await _cuentas.CerrarVacanteAsync(cerrada.HcId, analista.AnalistaId);

        var fila = Assert.Single(await _cuentas.ListarConDotacionAsync());

        Assert.Equal(1, fila.VacantesAbiertas);
    }

    public void Dispose() => _db.Dispose();
}
