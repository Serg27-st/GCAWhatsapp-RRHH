using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Servicios;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// T5.12 (FUN-20): corregir lo que se cargó mal. Hasta ahora una vacante con el enlace del formulario
/// equivocado, con el título mal escrito o cerrada por error no tenía arreglo desde la aplicación:
/// había que tocar la base.
/// </summary>
public class EdicionCuentasVacantesTests : IDisposable
{
    private const int CuentaId = 7;
    private const int AnalistaId = 10;

    private readonly RrhhDbContext _db;
    private readonly CuentaService _cuentas;

    public EdicionCuentasVacantesTests()
    {
        _db = new RrhhDbContext(new DbContextOptionsBuilder<RrhhDbContext>()
            .UseInMemoryDatabase($"edicion-{Guid.NewGuid()}")
            .Options);

        _db.Database.EnsureCreated();

        _db.Cuentas.Add(new Cuenta { CuentaId = CuentaId, Nombre = "Alicorp", Activo = true });

        _db.Hcs.AddRange(
            new Hc
            {
                HcId = 1, CuentaId = CuentaId, Titulo = "Operario de produccion",
                UrlJobForms = "https://forms.gle/operario", CodigoAviso = "K7M2QX",
                Estado = EstadoHc.Abierta, FechaCreacion = DateTime.UtcNow
            },
            new Hc
            {
                HcId = 2, CuentaId = CuentaId, Titulo = "Almacenero",
                UrlJobForms = "https://forms.gle/almacenero", CodigoAviso = "P9T4RW",
                Estado = EstadoHc.Abierta, FechaCreacion = DateTime.UtcNow
            });

        _db.SaveChanges();

        _cuentas = new CuentaService(_db, new AlertaOperativaService(_db, TimeProvider.System), TimeProvider.System);
    }

    private Task<Hc> VacanteAsync(int hcId = 1) =>
        _db.Hcs.AsNoTracking().FirstAsync(h => h.HcId == hcId);

    [Fact]
    public async Task Se_corrigen_titulo_enlace_y_codigo()
    {
        await _cuentas.ActualizarVacanteAsync(
            1, "Operario de producción", "https://forms.gle/operario-v2", "ABC234", AnalistaId);

        var vacante = await VacanteAsync();

        Assert.Equal("Operario de producción", vacante.Titulo);
        Assert.Equal("https://forms.gle/operario-v2", vacante.UrlJobForms);
        Assert.Equal("ABC234", vacante.CodigoAviso);

        Assert.True(await _db.Auditorias.AnyAsync(a => a.Accion == "VacanteEditada"));
    }

    [Fact]
    public async Task Lo_que_no_se_manda_no_se_toca()
    {
        await _cuentas.ActualizarVacanteAsync(1, titulo: null, urlJobForms: null, codigoAviso: null, AnalistaId);

        var vacante = await VacanteAsync();

        Assert.Equal("Operario de produccion", vacante.Titulo);
        Assert.Equal("https://forms.gle/operario", vacante.UrlJobForms);
        Assert.Equal("K7M2QX", vacante.CodigoAviso);
    }

    /// <summary>
    /// El enlace va en un mensaje de WhatsApp: uno sin https deja al postulante mandando su DNI y su CV
    /// por una conexión sin cifrar.
    /// </summary>
    [Theory]
    [InlineData("http://forms.gle/operario")]
    [InlineData("no-es-una-url")]
    public async Task Un_enlace_que_no_es_https_se_rechaza(string url)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _cuentas.ActualizarVacanteAsync(1, null, url, null, AnalistaId));

        Assert.Contains("https", ex.Message);
        Assert.Equal("https://forms.gle/operario", (await VacanteAsync()).UrlJobForms);
    }

    /// <summary>El código es lo que el postulante escribe: dos vacantes con el mismo mandarían al formulario equivocado.</summary>
    [Fact]
    public async Task Un_codigo_repetido_se_rechaza()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _cuentas.ActualizarVacanteAsync(1, null, null, "P9T4RW", AnalistaId));

        Assert.Contains("P9T4RW", ex.Message);
    }

    /// <summary>
    /// Lo que el postulante va a transcribir del aviso: sin signos, ni demasiado corto como para
    /// chocar con cualquier palabra suelta del mensaje.
    /// </summary>
    [Theory]
    [InlineData("ABC")]
    [InlineData("ABC-234")]
    [InlineData("OPERARIO PRODUCCION")]
    public async Task Un_codigo_que_no_sirve_como_codigo_se_rechaza(string codigo)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _cuentas.ActualizarVacanteAsync(1, null, null, codigo, AnalistaId));
    }

    /// <summary>
    /// Los generados evitan las letras que se confunden al dictar (I, L, O), pero uno escrito a mano
    /// vale igual: quien lo elige sabe lo que va a imprimir en el aviso (FUN-20).
    /// </summary>
    [Fact]
    public async Task Un_codigo_escrito_a_mano_con_letras_ambiguas_se_acepta()
    {
        await _cuentas.ActualizarVacanteAsync(1, null, null, "ALICORP1", AnalistaId);

        Assert.Equal("ALICORP1", (await VacanteAsync()).CodigoAviso);
    }

    /// <summary>Se acepta en minúscula: quien lo dicta por teléfono no piensa en mayúsculas.</summary>
    [Fact]
    public async Task El_codigo_se_normaliza()
    {
        await _cuentas.ActualizarVacanteAsync(1, null, null, "abc234", AnalistaId);

        Assert.Equal("ABC234", (await VacanteAsync()).CodigoAviso);
    }

    [Fact]
    public async Task Una_vacante_cerrada_por_error_se_reabre()
    {
        await _cuentas.CerrarVacanteAsync(1, AnalistaId);
        Assert.Equal(EstadoHc.Cerrada, (await VacanteAsync()).Estado);

        await _cuentas.ReabrirVacanteAsync(1, AnalistaId);

        var vacante = await VacanteAsync();

        Assert.Equal(EstadoHc.Abierta, vacante.Estado);
        Assert.Null(vacante.FechaCierre);
        Assert.True(await _db.Auditorias.AnyAsync(a => a.Accion == "VacanteReabierta"));
    }

    [Fact]
    public async Task Se_corrige_el_nombre_de_una_cuenta()
    {
        await _cuentas.ActualizarCuentaAsync(CuentaId, "Alicorp S.A.", activo: null, AnalistaId);

        var cuenta = await _db.Cuentas.AsNoTracking().FirstAsync(c => c.CuentaId == CuentaId);

        Assert.Equal("Alicorp S.A.", cuenta.Nombre);
        Assert.True(cuenta.Activo);
    }

    /// <summary>
    /// Desactivar una cuenta la saca del menú del bot, pero no mueve lo que ya está en curso: las
    /// conversaciones abiertas siguen con su analista, y queda una alerta para que alguien las cierre.
    /// </summary>
    [Fact]
    public async Task Desactivar_una_cuenta_con_conversaciones_deja_una_alerta()
    {
        _db.Conversaciones.Add(new Conversacion
        {
            TelefonoE164 = "+51987654321",
            CuentaContextoId = CuentaId,
            AnalistaAtendiendoId = AnalistaId,
            Estado = EstadoConversacion.Activa,
            FechaCreacion = DateTime.UtcNow,
            FechaUltimaActividad = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        await _cuentas.ActualizarCuentaAsync(CuentaId, nombre: null, activo: false, AnalistaId);

        Assert.False((await _db.Cuentas.AsNoTracking().FirstAsync(c => c.CuentaId == CuentaId)).Activo);

        var alerta = await _db.AlertasOperativas.AsNoTracking()
            .SingleAsync(a => a.Tipo == TiposAlerta.CuentaDesactivadaConConversaciones);

        Assert.Equal($"cuenta:{CuentaId}", alerta.Clave);

        // El hilo sigue donde estaba: moverlo sería decidir por el analista que lo está atendiendo.
        var hilo = await _db.Conversaciones.AsNoTracking().SingleAsync();
        Assert.Equal(AnalistaId, hilo.AnalistaAtendiendoId);
    }

    [Fact]
    public async Task Desactivar_una_cuenta_sin_conversaciones_no_alerta_de_nada()
    {
        await _cuentas.ActualizarCuentaAsync(CuentaId, nombre: null, activo: false, AnalistaId);

        Assert.Empty(await _db.AlertasOperativas.AsNoTracking().ToListAsync());
    }

    public void Dispose() => _db.Dispose();
}
