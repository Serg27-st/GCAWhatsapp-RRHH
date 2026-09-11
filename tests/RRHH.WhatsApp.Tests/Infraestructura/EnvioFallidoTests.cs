using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Servicios;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// Un saliente que el proveedor rechaza tiene que quedar registrado como fallido, con su motivo.
/// <para>
/// Antes se intentaba marcarlo por el id del proveedor, que en un envio rechazado no existe: la
/// busqueda no encontraba la fila, salia sin hacer nada, y el mensaje quedaba en Pendiente para
/// siempre con el error solo en el log. Es el fallo silencioso que la Seccion 9.6.2 quiere evitar.
/// </para>
/// </summary>
public class EnvioFallidoTests : IDisposable
{
    private readonly RrhhDbContext _db;
    private readonly MensajeService _mensajes;
    private readonly ConversacionService _conversaciones;

    public EnvioFallidoTests()
    {
        var opciones = new DbContextOptionsBuilder<RrhhDbContext>()
            .UseInMemoryDatabase($"envio-fallido-{Guid.NewGuid()}")
            .Options;

        _db = new RrhhDbContext(opciones);
        _db.Database.EnsureCreated();

        _mensajes = new MensajeService(_db, NullLogger<MensajeService>.Instance);
        _conversaciones = new ConversacionService(_db, NullLogger<ConversacionService>.Instance);
    }

    private async Task<long> CrearSalienteSinIdDeProveedorAsync()
    {
        var conversacion = await _conversaciones.ObtenerOCrearAsync("+51987654321");

        // providerMessageId nulo es exactamente lo que deja un envio rechazado.
        var mensaje = await _mensajes.RegistrarSalienteAsync(
            conversacion.ConversacionId, "Menu de empresas", null, null, null, Guid.NewGuid());

        return mensaje.MensajeId;
    }

    [Fact]
    public async Task Un_saliente_sin_id_de_proveedor_nace_pendiente()
    {
        var id = await CrearSalienteSinIdDeProveedorAsync();

        Assert.Equal(EstadoEntrega.Pendiente, _db.Mensajes.Single(m => m.MensajeId == id).EstadoEntrega);
    }

    [Fact]
    public async Task Marcar_el_envio_fallido_deja_el_estado_y_el_motivo_en_la_base()
    {
        var id = await CrearSalienteSinIdDeProveedorAsync();

        await _mensajes.MarcarEnvioFallidoAsync(id, "HTTP 400: recipient not in allowed list");

        var mensaje = _db.Mensajes.Single(m => m.MensajeId == id);

        Assert.Equal(EstadoEntrega.Fallido, mensaje.EstadoEntrega);
        Assert.Contains("recipient not in allowed list", mensaje.ErrorProveedor);
    }

    [Fact]
    public async Task El_acuse_por_id_de_proveedor_no_alcanza_cuando_no_hay_id()
    {
        // Es la razon de que exista MarcarEnvioFallidoAsync: por esta via el fallo se perdia.
        var id = await CrearSalienteSinIdDeProveedorAsync();

        await _mensajes.ActualizarEstadoEntregaAsync(
            new EstadoEntregaDto($"local-{id}", "failed", null, "lo que sea", DateTime.UtcNow));

        var mensaje = _db.Mensajes.Single(m => m.MensajeId == id);

        Assert.Equal(EstadoEntrega.Pendiente, mensaje.EstadoEntrega);
        Assert.Null(mensaje.ErrorProveedor);
    }

    [Fact]
    public async Task Sin_motivo_del_proveedor_igual_queda_un_texto_utilizable()
    {
        var id = await CrearSalienteSinIdDeProveedorAsync();

        await _mensajes.MarcarEnvioFallidoAsync(id, null);

        var mensaje = _db.Mensajes.Single(m => m.MensajeId == id);

        Assert.Equal(EstadoEntrega.Fallido, mensaje.EstadoEntrega);
        Assert.False(string.IsNullOrWhiteSpace(mensaje.ErrorProveedor));
    }

    [Fact]
    public async Task Un_error_larguisimo_no_revienta_la_columna()
    {
        // ErrorProveedor esta limitado a 500 caracteres en el esquema.
        var id = await CrearSalienteSinIdDeProveedorAsync();

        await _mensajes.MarcarEnvioFallidoAsync(id, new string('x', 2000));

        Assert.Equal(500, _db.Mensajes.Single(m => m.MensajeId == id).ErrorProveedor!.Length);
    }

    [Fact]
    public async Task Marcar_un_mensaje_inexistente_no_lanza()
    {
        await _mensajes.MarcarEnvioFallidoAsync(999_999, "no existe");
    }

    public void Dispose() => _db.Dispose();
}
