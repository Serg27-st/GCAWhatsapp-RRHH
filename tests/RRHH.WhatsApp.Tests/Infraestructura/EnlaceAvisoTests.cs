using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Api.Configuracion;
using RRHH.WhatsApp.Api.Controllers;
using RRHH.WhatsApp.Api.Seguridad;
using RRHH.WhatsApp.Contracts.Administracion;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Servicios;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// FUN-02 (A6): el enlace del aviso es lo que le ahorra al postulante el menú de ~20 empresas. Se
/// prueba sobre el controlador porque lo que importa es lo que la pantalla recibe: el código, el
/// enlace ya armado, y el aviso claro cuando falta configurar el número público.
/// </summary>
public class EnlaceAvisoTests : IDisposable
{
    private const int CuentaId = 7;
    private const int TitularId = 10;
    private const string Codigo = "K7M2QX";

    private readonly RrhhDbContext _db;

    public EnlaceAvisoTests()
    {
        _db = new RrhhDbContext(new DbContextOptionsBuilder<RrhhDbContext>()
            .UseInMemoryDatabase($"enlace-{Guid.NewGuid()}")
            .Options);

        _db.Database.EnsureCreated();

        _db.Cuentas.Add(new Cuenta { CuentaId = CuentaId, Nombre = "Alicorp", Activo = true });

        _db.Analistas.Add(new Analista
        {
            AnalistaId = TitularId, Nombre = "Ana Torres", Email = "ana@gca.pe", Activo = true
        });

        _db.AnalistaCuentas.Add(new AnalistaCuenta
        {
            AnalistaCuentaId = 1, AnalistaId = TitularId, CuentaId = CuentaId, EsBackup = false
        });

        _db.Hcs.Add(new Hc
        {
            HcId = 1,
            CuentaId = CuentaId,
            Titulo = "Operario de produccion",
            UrlJobForms = "https://forms.gle/operario",
            CodigoAviso = Codigo,
            Estado = EstadoHc.Abierta,
            FechaCreacion = DateTime.UtcNow
        });

        _db.SaveChanges();
    }

    private VacantesController Controlador(string numeroPublico, int analistaId = TitularId, string rol = "Analista")
    {
        var cuentas = new CuentaService(_db, new AlertaOperativaService(_db, TimeProvider.System), TimeProvider.System);

        return new VacantesController(
            cuentas,
            new PostulacionService(_db, TimeProvider.System, NullLogger<PostulacionService>.Instance),
            Options.Create(new OpcionesWhatsApp { NumeroPublico = numeroPublico }),
            NullLogger<VacantesController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [
                            new Claim(ClaimsAnalista.AnalistaId, analistaId.ToString()),
                            new Claim(ClaimsAnalista.Rol, rol)
                        ],
                        "prueba"))
                }
            }
        };
    }

    private static EnlaceAviso Leer(IActionResult resultado) =>
        Assert.IsType<EnlaceAviso>(Assert.IsType<OkObjectResult>(resultado).Value);

    /// <summary>El enlace deja el mensaje escrito: el postulante solo toca enviar.</summary>
    [Fact]
    public async Task El_enlace_lleva_el_numero_sin_signos_y_el_codigo_en_el_texto()
    {
        var resultado = await Controlador("+51999888777").EnlaceDeAviso(1, default);

        var aviso = Leer(resultado);

        Assert.Equal(Codigo, aviso.Codigo);
        Assert.Equal($"https://wa.me/51999888777?text=Hola%2C%20postulo%20a%20{Codigo}", aviso.Enlace);
    }

    /// <summary>El número lo escribe una persona en la configuración: puede venir con paréntesis y guiones.</summary>
    [Theory]
    [InlineData("+1 (555) 665-0366")]
    [InlineData("+1-555-665-0366")]
    [InlineData("15556650366")]
    public async Task El_numero_se_limpia_antes_de_armar_el_enlace(string configurado)
    {
        var aviso = Leer(await Controlador(configurado).EnlaceDeAviso(1, default));

        Assert.StartsWith("https://wa.me/15556650366?text=", aviso.Enlace);
    }

    /// <summary>
    /// Sin número configurado se devuelve el código igual: sirve para escribirlo en el aviso a mano.
    /// El enlace nulo es lo que hace que la pantalla avise en vez de ofrecer uno que no lleva a nada.
    /// </summary>
    [Fact]
    public async Task Sin_numero_publico_hay_codigo_pero_no_enlace()
    {
        var aviso = Leer(await Controlador(string.Empty).EnlaceDeAviso(1, default));

        Assert.Equal(Codigo, aviso.Codigo);
        Assert.Null(aviso.Enlace);
    }

    [Fact]
    public async Task Una_vacante_sin_codigo_no_tiene_enlace_que_dar()
    {
        var vacante = await _db.Hcs.FirstAsync();
        vacante.CodigoAviso = null;

        await _db.SaveChangesAsync();

        Assert.IsType<UnprocessableEntityObjectResult>(
            await Controlador("+51999888777").EnlaceDeAviso(1, default));
    }

    [Fact]
    public async Task Una_vacante_que_no_existe_da_404()
    {
        Assert.IsType<NotFoundObjectResult>(await Controlador("+51999888777").EnlaceDeAviso(99, default));
    }

    /// <summary>V23: el enlace lo ven quienes manejan la vacante, igual que para editarla.</summary>
    [Fact]
    public async Task Quien_no_trabaja_la_cuenta_no_ve_el_enlace()
    {
        _db.Analistas.Add(new Analista
        {
            AnalistaId = 12, Nombre = "Rosa Diaz", Email = "rosa@gca.pe", Activo = true
        });

        await _db.SaveChangesAsync();

        var resultado = await Controlador("+51999888777", analistaId: 12).EnlaceDeAviso(1, default);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(resultado).StatusCode);
    }

    [Fact]
    public async Task Sistemas_si_lo_ve_como_soporte()
    {
        var resultado = await Controlador("+51999888777", analistaId: 12, rol: "Sistemas").EnlaceDeAviso(1, default);

        Assert.IsType<OkObjectResult>(resultado);
    }

    public void Dispose() => _db.Dispose();
}
