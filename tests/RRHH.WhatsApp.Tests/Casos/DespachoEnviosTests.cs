using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Servicios;
using RRHH.WhatsApp.Tests.Proveedores;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// T1.08 (V29, ARQ-03): el despachador saca lo encolado, revalida la Regla 15 al momento de enviar y
/// clasifica el resultado. Entre que una regla decidió y el despacho pudo cerrarse la ventana o
/// desactivarse la plantilla: lo que era legal al encolar puede no serlo al salir.
/// </summary>
public class DespachoEnviosTests : IDisposable
{
    private static readonly TimeSpan TimeoutEnviando = TimeSpan.FromMinutes(2);

    private readonly RrhhDbContext _db;
    private readonly FakeTimeProvider _reloj = new(new DateTimeOffset(2026, 9, 14, 15, 0, 0, TimeSpan.Zero));
    private readonly MensajeService _mensajes;
    private readonly ConversacionService _conversaciones;
    private readonly ProveedorFalso _proveedor = new();

    public DespachoEnviosTests()
    {
        _db = new RrhhDbContext(new DbContextOptionsBuilder<RrhhDbContext>()
            .UseInMemoryDatabase($"despacho-{Guid.NewGuid()}")
            .Options);

        _db.Database.EnsureCreated();

        _mensajes = new MensajeService(_db, _reloj, NullLogger<MensajeService>.Instance);
        _conversaciones = ServiciosDePrueba.Conversaciones(_db, _reloj);
    }

    private DateTime Ahora => _reloj.GetUtcNow().UtcDateTime;

    private DespachoEnvios Despacho()
    {
        var plantillas = new PlantillaService(_db, _reloj, NullLogger<PlantillaService>.Instance);

        return new DespachoEnvios(
            _mensajes, _conversaciones, plantillas, _proveedor,
            new ValidadorEnvio(_conversaciones, plantillas),
            new ConfiguracionReglasService(_db, new MemoryCache(new MemoryCacheOptions()), _reloj),
            _reloj,
            NullLogger<DespachoEnvios>.Instance);
    }

    private async Task<Conversacion> ConversacionAsync(bool ventanaAbierta = true)
    {
        var conversacion = await _conversaciones.ObtenerOCrearAsync("+51987654321");

        conversacion.FechaOptIn = Ahora.AddDays(-1);
        conversacion.FechaUltimoMensajeEntrante = ventanaAbierta ? Ahora.AddHours(-1) : Ahora.AddHours(-30);
        await _db.SaveChangesAsync();

        return conversacion;
    }

    private Mensaje Releer() => _db.Mensajes.AsNoTracking().Single();

    [Fact]
    public async Task Un_texto_encolado_sale_y_queda_enviado()
    {
        var conversacion = await ConversacionAsync();
        await _mensajes.EncolarSalienteAsync(conversacion.ConversacionId, new SalienteEncolado(TipoSaliente.Texto, "Hola"), "evt:1:0", null, Guid.NewGuid());
        _proveedor.Respuesta = ResultadoEnvio.Ok("wamid.despacho");

        var resumen = await Despacho().ProcesarAsync(10, TimeoutEnviando);

        Assert.Equal(1, resumen.Logrados);
        Assert.Equal(["texto"], _proveedor.Envios);

        var mensaje = Releer();
        Assert.Equal(EstadoEntrega.Enviado, mensaje.EstadoEntrega);
        Assert.Equal("wamid.despacho", mensaje.ProviderMessageId);
    }

    [Fact]
    public async Task Un_menu_encolado_sale_como_botones_con_sus_opciones()
    {
        var conversacion = await ConversacionAsync();
        var menu = new SalienteEncolado(TipoSaliente.Botones, "Elige una empresa",
            Opciones: [new BotonRespuesta("cuenta_7", "Alicorp")]);

        await _mensajes.EncolarSalienteAsync(conversacion.ConversacionId, menu, "evt:2:0", null, Guid.NewGuid());

        await Despacho().ProcesarAsync(10, TimeoutEnviando);

        Assert.Equal(["botones"], _proveedor.Envios);
    }

    [Fact]
    public async Task Una_plantilla_que_se_desactivo_no_sale_y_queda_permanente()
    {
        var conversacion = await ConversacionAsync();

        // Todas las plantillas sembradas están inactivas (V8): nunca se aprobaron en Meta.
        var plantilla = await _db.Plantillas.AsNoTracking().FirstAsync();
        await _mensajes.EncolarSalienteAsync(conversacion.ConversacionId,
            new SalienteEncolado(TipoSaliente.Plantilla, plantilla.TextoAprobado, plantilla.PlantillaId), "evt:3:0", null, Guid.NewGuid());

        var resumen = await Despacho().ProcesarAsync(10, TimeoutEnviando);

        Assert.Equal(0, _proveedor.Llamadas);
        Assert.Equal(1, resumen.Fallidos);

        var mensaje = Releer();
        Assert.Equal(EstadoEntrega.Fallido, mensaje.EstadoEntrega);
        Assert.Equal(ClaseFallo.Permanente, mensaje.ClaseFallo);
        Assert.Null(mensaje.ProximoIntentoUtc);
    }

    [Fact]
    public async Task Un_texto_con_la_ventana_cerrada_al_despachar_no_sale()
    {
        // Se encoló con la ventana abierta y el despacho llegó tarde: salir sería texto libre
        // fuera de las 24h, lo que Meta sanciona (Regla 15).
        var conversacion = await ConversacionAsync(ventanaAbierta: true);
        await _mensajes.EncolarSalienteAsync(conversacion.ConversacionId, new SalienteEncolado(TipoSaliente.Texto, "Hola"), "evt:4:0", null, Guid.NewGuid());

        _reloj.Advance(TimeSpan.FromHours(24));

        await Despacho().ProcesarAsync(10, TimeoutEnviando);

        Assert.Equal(0, _proveedor.Llamadas);

        var mensaje = Releer();
        Assert.Equal(ClaseFallo.Permanente, mensaje.ClaseFallo);
        Assert.Contains("ventana de 24h", mensaje.ErrorProveedor);
    }

    [Fact]
    public async Task Un_503_queda_transitorio_con_proximo_intento()
    {
        var conversacion = await ConversacionAsync();
        await _mensajes.EncolarSalienteAsync(conversacion.ConversacionId, new SalienteEncolado(TipoSaliente.Texto, "Hola"), "evt:5:0", null, Guid.NewGuid());
        _proveedor.Respuesta = ResultadoEnvio.Transitorio("HTTP 503");

        await Despacho().ProcesarAsync(10, TimeoutEnviando);

        var mensaje = Releer();
        Assert.Equal(EstadoEntrega.Fallido, mensaje.EstadoEntrega);
        Assert.Equal(ClaseFallo.Transitorio, mensaje.ClaseFallo);

        // envio.reintento_base_segundos vale 60 en la semilla: ReintentoEnvios lo toma desde ahí.
        Assert.Equal(Ahora.AddSeconds(60), mensaje.ProximoIntentoUtc);
    }

    [Fact]
    public async Task Un_ambiguo_no_se_agenda()
    {
        var conversacion = await ConversacionAsync();
        await _mensajes.EncolarSalienteAsync(conversacion.ConversacionId, new SalienteEncolado(TipoSaliente.Texto, "Hola"), "evt:6:0", null, Guid.NewGuid());
        _proveedor.Respuesta = ResultadoEnvio.Ambiguo("Sin respuesta");

        await Despacho().ProcesarAsync(10, TimeoutEnviando);

        var mensaje = Releer();
        Assert.Equal(ClaseFallo.Ambiguo, mensaje.ClaseFallo);
        Assert.Null(mensaje.ProximoIntentoUtc);
    }

    [Fact]
    public async Task Antes_de_tomar_el_lote_recupera_los_enviando_atascados()
    {
        var conversacion = await ConversacionAsync();
        await _mensajes.EncolarSalienteAsync(conversacion.ConversacionId, new SalienteEncolado(TipoSaliente.Texto, "Hola"), "evt:7:0", null, Guid.NewGuid());
        await _mensajes.TomarLoteEnColaAsync(10);

        _reloj.Advance(TimeSpan.FromMinutes(5));

        var resumen = await Despacho().ProcesarAsync(10, TimeoutEnviando);

        Assert.Equal(1, resumen.Recuperados);
        Assert.Equal(0, _proveedor.Llamadas);
        Assert.Equal(ClaseFallo.Ambiguo, Releer().ClaseFallo);
    }

    [Fact]
    public void El_ejecutor_de_acciones_no_puede_enviar()
    {
        // V29: lo que deciden las reglas se encola. Si el ejecutor vuelve a recibir el proveedor,
        // un fallo posterior al envío vuelve a mandar el mensaje al reprocesar el evento (C5).
        var dependencias = typeof(EjecutorAcciones).GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType);

        Assert.DoesNotContain(typeof(IWhatsAppProvider), dependencias);
    }

    public void Dispose() => _db.Dispose();
}
