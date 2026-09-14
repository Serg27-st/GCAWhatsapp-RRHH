using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Proveedores;
using RRHH.WhatsApp.Infrastructure.Servicios;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// Politica de reintento del saliente. Lo que se prueba no es que reintente, sino que reintente
/// solo cuando es seguro: el proyecto existe porque a Meta le bloquearon la linea, y un reenvio
/// indebido es peor que un mensaje perdido.
/// </summary>
public class ReintentoEnviosTests : IDisposable
{
    private readonly RrhhDbContext _db;
    private readonly MensajeService _mensajes;
    private readonly ConversacionService _conversaciones;
    private readonly PlantillaService _plantillas;
    private readonly ProveedorFalso _proveedor = new();

    public ReintentoEnviosTests()
    {
        var opciones = new DbContextOptionsBuilder<RrhhDbContext>()
            .UseInMemoryDatabase($"reintentos-{Guid.NewGuid()}")
            .Options;

        _db = new RrhhDbContext(opciones);
        _db.Database.EnsureCreated();

        _mensajes = new MensajeService(_db, NullLogger<MensajeService>.Instance);
        _conversaciones = new ConversacionService(_db, NullLogger<ConversacionService>.Instance);
        _plantillas = new PlantillaService(_db, NullLogger<PlantillaService>.Instance);
    }

    private ReintentoEnvios Crear() => new(
        _mensajes, _conversaciones, _plantillas, _proveedor,
        new ConfiguracionFalsa(_db), NullLogger<ReintentoEnvios>.Instance);

    /// <summary>Deja un saliente ya fallido por causa transitoria y con el intento vencido.</summary>
    private async Task<Mensaje> PrepararFallidoAsync(
        bool ventanaAbierta = true, bool conOptIn = true, int? plantillaId = null, int intentos = 1)
    {
        var conversacion = await _conversaciones.ObtenerOCrearAsync("+51987654321");

        conversacion.FechaOptIn = conOptIn ? DateTime.UtcNow.AddDays(-3) : null;
        conversacion.FechaUltimoMensajeEntrante = ventanaAbierta
            ? DateTime.UtcNow.AddHours(-2)
            : DateTime.UtcNow.AddHours(-30);

        await _db.SaveChangesAsync();

        var mensaje = await _mensajes.RegistrarSalienteAsync(
            conversacion.ConversacionId, "Menu de empresas", plantillaId, null, null, Guid.NewGuid());

        mensaje.EstadoEntrega = EstadoEntrega.Fallido;
        mensaje.ClaseFallo = ClaseFallo.Transitorio;
        mensaje.IntentosEnvio = intentos;
        mensaje.ProximoIntentoUtc = DateTime.UtcNow.AddMinutes(-1);
        await _db.SaveChangesAsync();

        return mensaje;
    }

    private Mensaje Releer(long id) => _db.Mensajes.Single(m => m.MensajeId == id);

    [Fact]
    public async Task Un_fallo_transitorio_se_reintenta_y_al_lograrlo_queda_enviado()
    {
        var mensaje = await PrepararFallidoAsync();
        _proveedor.Respuesta = ResultadoEnvio.Ok("wamid.REINTENTO");

        var resumen = await Crear().ProcesarAsync(10);

        Assert.Equal(1, resumen.Logrados);

        var releido = Releer(mensaje.MensajeId);
        Assert.Equal(EstadoEntrega.Enviado, releido.EstadoEntrega);
        Assert.Equal("wamid.REINTENTO", releido.ProviderMessageId);
        Assert.Null(releido.ProximoIntentoUtc);
        Assert.Null(releido.ErrorProveedor);
    }

    [Fact]
    public async Task Si_vuelve_a_fallar_se_reprograma_con_retroceso()
    {
        var mensaje = await PrepararFallidoAsync(intentos: 1);
        _proveedor.Respuesta = ResultadoEnvio.Transitorio("HTTP 503");

        var antes = DateTime.UtcNow;
        var resumen = await Crear().ProcesarAsync(10);

        Assert.Equal(1, resumen.Reprogramados);

        var releido = Releer(mensaje.MensajeId);
        Assert.Equal(2, releido.IntentosEnvio);
        Assert.NotNull(releido.ProximoIntentoUtc);
        // Base 60s con dos intentos usados: el retroceso ya es de al menos dos minutos.
        Assert.True(releido.ProximoIntentoUtc > antes.AddMinutes(1.5));
    }

    [Fact]
    public async Task Agotados_los_intentos_deja_de_reintentar()
    {
        // envio.reintentos_maximos vale 4 en la semilla.
        var mensaje = await PrepararFallidoAsync(intentos: 3);
        _proveedor.Respuesta = ResultadoEnvio.Transitorio("HTTP 503");

        var resumen = await Crear().ProcesarAsync(10);

        Assert.Equal(1, resumen.Abandonados);
        Assert.Null(Releer(mensaje.MensajeId).ProximoIntentoUtc);
    }

    [Fact]
    public async Task Un_fallo_permanente_no_se_vuelve_a_agendar()
    {
        // Token vencido entre un intento y el siguiente: seguir insistiendo solo gasta cuota.
        var mensaje = await PrepararFallidoAsync();
        _proveedor.Respuesta = ResultadoEnvio.Permanente("Authentication Error");

        var resumen = await Crear().ProcesarAsync(10);

        Assert.Equal(1, resumen.Abandonados);

        var releido = Releer(mensaje.MensajeId);
        Assert.Equal(ClaseFallo.Permanente, releido.ClaseFallo);
        Assert.Null(releido.ProximoIntentoUtc);
    }

    [Fact]
    public async Task Un_texto_libre_no_se_reenvia_si_la_ventana_de_24h_se_cerro()
    {
        // El punto central: entre el primer intento y este pasaron mas de 24h desde el ultimo
        // mensaje del postulante, asi que reenviarlo como texto libre violaria la Regla 15.
        var mensaje = await PrepararFallidoAsync(ventanaAbierta: false);
        _proveedor.Respuesta = ResultadoEnvio.Ok("no-deberia-usarse");

        var resumen = await Crear().ProcesarAsync(10);

        Assert.Equal(1, resumen.Abandonados);
        Assert.Equal(0, _proveedor.Llamadas);

        var releido = Releer(mensaje.MensajeId);
        Assert.Equal(ClaseFallo.Permanente, releido.ClaseFallo);
        Assert.Contains("ventana de 24h", releido.ErrorProveedor);
    }

    [Fact]
    public async Task Sin_optin_no_se_reintenta_aunque_el_fallo_fuera_transitorio()
    {
        var mensaje = await PrepararFallidoAsync(conOptIn: false);
        _proveedor.Respuesta = ResultadoEnvio.Ok("no-deberia-usarse");

        var resumen = await Crear().ProcesarAsync(10);

        Assert.Equal(1, resumen.Abandonados);
        Assert.Equal(0, _proveedor.Llamadas);
        Assert.Contains("opt-in", Releer(mensaje.MensajeId).ErrorProveedor);
    }

    [Fact]
    public async Task Un_fallo_ambiguo_nunca_entra_al_barrido()
    {
        // La Cloud API no admite clave de idempotencia: si se perdio la respuesta, el mensaje pudo
        // haber salido y reintentarlo lo duplicaria.
        var mensaje = await PrepararFallidoAsync();

        var fila = Releer(mensaje.MensajeId);
        fila.ClaseFallo = ClaseFallo.Ambiguo;
        await _db.SaveChangesAsync();

        var resumen = await Crear().ProcesarAsync(10);

        Assert.Equal(0, resumen.Intentados);
        Assert.Equal(0, _proveedor.Llamadas);
    }

    [Fact]
    public async Task No_toca_los_mensajes_cuyo_proximo_intento_todavia_no_vencio()
    {
        var mensaje = await PrepararFallidoAsync();

        var fila = Releer(mensaje.MensajeId);
        fila.ProximoIntentoUtc = DateTime.UtcNow.AddMinutes(30);
        await _db.SaveChangesAsync();

        Assert.Equal(0, (await Crear().ProcesarAsync(10)).Intentados);
    }

    [Fact]
    public async Task Sin_nada_pendiente_no_llama_al_proveedor()
    {
        Assert.Equal(ResumenReintentos.Vacio, await Crear().ProcesarAsync(10));
        Assert.Equal(0, _proveedor.Llamadas);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>Proveedor de prueba: devuelve lo que se le indique y cuenta cuantas veces lo llamaron.</summary>
    private sealed class ProveedorFalso : IWhatsAppProvider
    {
        public ResultadoEnvio Respuesta { get; set; } = ResultadoEnvio.Ok("id");
        public int Llamadas { get; private set; }

        public string Nombre => "falso";

        public Task<ResultadoEnvio> EnviarTextoAsync(string t, string x, CancellationToken ct = default)
        {
            Llamadas++;
            return Task.FromResult(Respuesta);
        }

        public Task<ResultadoEnvio> EnviarPlantillaAsync(
            string t, Plantilla p, IReadOnlyList<string> par, CancellationToken ct = default)
        {
            Llamadas++;
            return Task.FromResult(Respuesta);
        }

        public Task<ResultadoEnvio> EnviarBotonesAsync(
            string t, string x, IReadOnlyList<BotonRespuesta> b, CancellationToken ct = default)
        {
            Llamadas++;
            return Task.FromResult(Respuesta);
        }

        public Task<ResultadoEnvio> EnviarListaAsync(
            string t, string x, string b, IReadOnlyList<BotonRespuesta> o, CancellationToken ct = default)
        {
            Llamadas++;
            return Task.FromResult(Respuesta);
        }

        public bool ValidarFirma(string c, IReadOnlyDictionary<string, string> h) => true;

        public IReadOnlyList<MensajeEntranteDto> InterpretarWebhook(string c) => [];

        public IReadOnlyList<EstadoEntregaDto> InterpretarEstados(string c) => [];
    }

    /// <summary>Lee la configuracion sembrada, sin cache: las pruebas cambian valores entre casos.</summary>
    private sealed class ConfiguracionFalsa(RrhhDbContext db) : IConfiguracionReglasService
    {
        public async Task<IReadOnlyDictionary<string, string>> ObtenerTodasAsync(CancellationToken ct = default) =>
            await db.ConfiguracionReglas.AsNoTracking().ToDictionaryAsync(c => c.Clave, c => c.Valor, ct);

        public async Task<IReadOnlyList<ConfiguracionRegla>> ListarAsync(CancellationToken ct = default) =>
            await db.ConfiguracionReglas.AsNoTracking().ToListAsync(ct);

        public Task EstablecerAsync(string clave, string valor, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
