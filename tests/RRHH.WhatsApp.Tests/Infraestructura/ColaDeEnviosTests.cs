using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Servicios;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// T1.07 (V29, ARQ-03): la cola de envíos en <c>Mensajes</c>. Decidir un envío solo escribe una fila
/// <c>EnCola</c> con clave única; lo manda después el despachador. Así reprocesar un evento no
/// vuelve a mandar el mensaje, que es el patrón de duplicados que costó la línea.
/// </summary>
public class ColaDeEnviosTests : IDisposable
{
    private readonly RrhhDbContext _db;
    private readonly FakeTimeProvider _reloj = new(new DateTimeOffset(2026, 9, 14, 15, 0, 0, TimeSpan.Zero));
    private readonly MensajeService _mensajes;
    private readonly ConversacionService _conversaciones;

    public ColaDeEnviosTests()
    {
        _db = new RrhhDbContext(new DbContextOptionsBuilder<RrhhDbContext>()
            .UseInMemoryDatabase($"cola-{Guid.NewGuid()}")
            .Options);

        _db.Database.EnsureCreated();

        _mensajes = new MensajeService(_db, _reloj, NullLogger<MensajeService>.Instance);
        _conversaciones = ServiciosDePrueba.Conversaciones(_db, _reloj);
    }

    private async Task<int> ConversacionAsync() =>
        (await _conversaciones.ObtenerOCrearAsync("+51987654321")).ConversacionId;

    private static SalienteEncolado Texto(string contenido = "Hola") => new(TipoSaliente.Texto, contenido);

    [Fact]
    public async Task Encolar_dos_veces_con_la_misma_clave_deja_una_sola_fila()
    {
        var id = await ConversacionAsync();

        var primero = await _mensajes.EncolarSalienteAsync(id, Texto(), "evt:15:0", null, Guid.NewGuid());
        var segundo = await _mensajes.EncolarSalienteAsync(id, Texto(), "evt:15:0", null, Guid.NewGuid());

        Assert.NotNull(primero);
        Assert.Null(segundo);
        Assert.Single(_db.Mensajes);
        Assert.Equal(EstadoEntrega.EnCola, primero.EstadoEntrega);
    }

    [Fact]
    public async Task Encolar_guarda_lo_necesario_para_rearmar_un_menu()
    {
        var id = await ConversacionAsync();

        var saliente = new SalienteEncolado(
            TipoSaliente.Lista, "Elige una empresa",
            Opciones: [new BotonRespuesta("cuenta_7", "Alicorp"), new BotonRespuesta("cuenta_8", "Intradevco")],
            TextoBotonLista: "Ver empresas");

        var mensaje = await _mensajes.EncolarSalienteAsync(id, saliente, "evt:16:0", null, Guid.NewGuid());

        Assert.Equal(TipoSaliente.Lista, mensaje!.TipoSaliente);
        Assert.Contains("cuenta_8", mensaje.OpcionesJson);
        Assert.Contains("Ver empresas", mensaje.OpcionesJson);
    }

    [Fact]
    public async Task La_respuesta_del_analista_nace_reservada_y_el_despachador_no_la_toma()
    {
        var id = await ConversacionAsync();

        await _mensajes.EncolarSalienteAsync(id, Texto(), "ana:1", 10, Guid.NewGuid(), reservadoParaEnvio: true);

        Assert.Empty(await _mensajes.TomarLoteEnColaAsync(10));
        Assert.Equal(EstadoEntrega.Enviando, _db.Mensajes.Single().EstadoEntrega);
    }

    [Fact]
    public async Task Tomar_el_lote_pasa_a_enviando_en_orden_de_llegada()
    {
        var id = await ConversacionAsync();

        await _mensajes.EncolarSalienteAsync(id, Texto("primero"), "evt:1:0", null, Guid.NewGuid());
        _reloj.Advance(TimeSpan.FromSeconds(1));
        await _mensajes.EncolarSalienteAsync(id, Texto("segundo"), "evt:2:0", null, Guid.NewGuid());

        var lote = await _mensajes.TomarLoteEnColaAsync(1);

        Assert.Equal("primero", Assert.Single(lote).Contenido);
        Assert.Equal(EstadoEntrega.Enviando, _db.Mensajes.AsNoTracking().Single(m => m.Contenido == "primero").EstadoEntrega);
        Assert.Equal(EstadoEntrega.EnCola, _db.Mensajes.AsNoTracking().Single(m => m.Contenido == "segundo").EstadoEntrega);
    }

    [Fact]
    public async Task Un_enviando_vencido_queda_ambiguo_sin_reintento()
    {
        var id = await ConversacionAsync();

        await _mensajes.EncolarSalienteAsync(id, Texto(), "evt:3:0", null, Guid.NewGuid());
        await _mensajes.TomarLoteEnColaAsync(10);

        _reloj.Advance(TimeSpan.FromMinutes(3));

        var recuperados = await _mensajes.RecuperarEnviandoVencidosAsync(TimeSpan.FromMinutes(2));

        var mensaje = _db.Mensajes.AsNoTracking().Single();
        Assert.Equal(1, recuperados);
        Assert.Equal(EstadoEntrega.Fallido, mensaje.EstadoEntrega);
        Assert.Equal(ClaseFallo.Ambiguo, mensaje.ClaseFallo);
        Assert.Null(mensaje.ProximoIntentoUtc);
    }

    [Fact]
    public async Task Un_enviando_reciente_no_se_toca()
    {
        var id = await ConversacionAsync();

        await _mensajes.EncolarSalienteAsync(id, Texto(), "evt:4:0", null, Guid.NewGuid());
        await _mensajes.TomarLoteEnColaAsync(10);

        _reloj.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(0, await _mensajes.RecuperarEnviandoVencidosAsync(TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public async Task Un_acuse_sobre_un_mensaje_enviando_lo_hace_avanzar()
    {
        // El envío en línea del analista puede recibir el acuse "sent" antes de marcar el resultado.
        // Con la comparación numérica vieja, Enviado (2) < Enviando (7) y el acuse se ignoraba.
        var id = await ConversacionAsync();

        var mensaje = await _mensajes.EncolarSalienteAsync(id, Texto(), "ana:2", 10, Guid.NewGuid(), reservadoParaEnvio: true);
        await _mensajes.MarcarEnvioLogradoAsync(mensaje!.MensajeId, "wamid.saliente.1");

        await _mensajes.ActualizarEstadoEntregaAsync(new EstadoEntregaDto("wamid.saliente.1", "delivered", null, null, _reloj.GetUtcNow().UtcDateTime));

        Assert.Equal(EstadoEntrega.Entregado, _db.Mensajes.AsNoTracking().Single().EstadoEntrega);
    }

    [Fact]
    public async Task Un_acuse_atrasado_no_retrocede_el_estado()
    {
        var id = await ConversacionAsync();

        var mensaje = await _mensajes.EncolarSalienteAsync(id, Texto(), "ana:3", 10, Guid.NewGuid(), reservadoParaEnvio: true);
        await _mensajes.MarcarEnvioLogradoAsync(mensaje!.MensajeId, "wamid.saliente.2");

        await _mensajes.ActualizarEstadoEntregaAsync(new EstadoEntregaDto("wamid.saliente.2", "read", null, null, _reloj.GetUtcNow().UtcDateTime));
        await _mensajes.ActualizarEstadoEntregaAsync(new EstadoEntregaDto("wamid.saliente.2", "sent", null, null, _reloj.GetUtcNow().UtcDateTime));

        Assert.Equal(EstadoEntrega.Leido, _db.Mensajes.AsNoTracking().Single().EstadoEntrega);
    }

    public void Dispose() => _db.Dispose();
}
