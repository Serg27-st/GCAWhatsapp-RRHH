using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Tests.Proveedores;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// El tramo que faltaba: lo que el webhook deja en la outbox termina evaluado por el motor y
/// ejecutado. Antes de esto las reglas existian y se probaban aisladas, pero nada las disparaba.
/// </summary>
public class ProcesadorOutboxTests : IDisposable
{
    private readonly EntornoDeReglas _entorno = new();

    private static Dictionary<string, string> SinCabeceras() => [];

    [Fact]
    public async Task El_webhook_deja_el_evento_pendiente_y_el_procesador_lo_consume()
    {
        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);

        var antes = await _entorno.Eventos.ObtenerPendientesAsync(10, ProcesadorOutbox.TiposQueAtiende);
        Assert.Single(antes);

        await _entorno.ConsumirOutboxAsync();

        var despues = await _entorno.Eventos.ObtenerPendientesAsync(10, ProcesadorOutbox.TiposQueAtiende);
        Assert.Empty(despues);

        var evento = await _entorno.Db.EventosSistema
            .FirstAsync(e => e.Tipo == TiposEvento.MensajeEntranteRecibido);

        Assert.Equal(EstadoEvento.Procesado, evento.Estado);
        Assert.NotNull(evento.FechaProcesado);
    }

    [Fact]
    public async Task Regla_1_al_elegir_la_empresa_la_conversacion_queda_asignada_a_su_analista()
    {
        // El boton "cuenta_7" es el menu de empresas resuelto: a partir de ahi la Regla 1 sabe a
        // quien enrutar.
        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);
        await _entorno.ConsumirOutboxAsync();

        var conversacion = await _entorno.Db.Conversaciones.FirstAsync();

        Assert.Equal(EntornoDeReglas.CuentaId, conversacion.CuentaContextoId);
        Assert.Equal(EntornoDeReglas.TitularId, conversacion.AnalistaAtendiendoId);
        Assert.Equal(EstadoConversacion.Activa, conversacion.Estado);
    }

    [Fact]
    public async Task Regla_19_un_mensaje_de_texto_libre_recibe_el_menu_de_empresas()
    {
        // Sin cuenta identificada manda la Regla 19: el bot muestra el menu en vez de enrutar.
        await _entorno.IngresarAsync(PayloadsDePrueba.MensajeDeTexto);
        await _entorno.ConsumirOutboxAsync();

        var envio = Assert.Single(_entorno.Proveedor.Enviados);

        Assert.Equal("botones", envio.Tipo);
        Assert.Equal(IdsBoton.ParaCuenta(EntornoDeReglas.CuentaId), envio.Detalle);

        var conversacion = await _entorno.Db.Conversaciones.FirstAsync();
        Assert.Null(conversacion.CuentaContextoId);
    }

    [Fact]
    public async Task El_menu_se_registra_en_el_hilo_para_que_la_bandeja_lo_muestre()
    {
        await _entorno.IngresarAsync(PayloadsDePrueba.MensajeDeTexto);
        await _entorno.ConsumirOutboxAsync();

        var saliente = await _entorno.Db.Mensajes
            .FirstAsync(m => m.Direccion == DireccionMensaje.Saliente);

        Assert.Contains("asistente automatico", saliente.Contenido);
    }

    [Fact]
    public async Task Un_payload_sin_conversacion_falla_en_vez_de_darse_por_procesado()
    {
        // Si el evento no se puede interpretar tiene que quedar visible, no desaparecer: es la
        // diferencia entre un fallo que se investiga y uno que nadie nota.
        var evento = new EventoSistema
        {
            EventoId = 99,
            Tipo = TiposEvento.MensajeEntranteRecibido,
            Payload = """{"telefonoE164":"+51987654321"}""",
            CorrelationId = Guid.NewGuid(),
            FechaCreacion = DateTime.UtcNow
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _entorno.Procesador.ProcesarAsync(evento));
    }

    [Fact]
    public async Task Un_tipo_que_todavia_no_tiene_duenno_no_lo_toma_este_procesador()
    {
        // AnalistaNotificado espera a la bandeja por SignalR. Mientras no exista, el evento se
        // queda Pendiente y fuera del lote, sin taponar la cola ni contarse como fallido.
        await _entorno.Eventos.PublicarAsync(
            TiposEvento.AnalistaNotificado, new { AnalistaId = 10, Mensaje = "algo" }, Guid.NewGuid());

        var lote = await _entorno.Eventos.ObtenerPendientesAsync(10, ProcesadorOutbox.TiposQueAtiende);
        Assert.Empty(lote);

        var todos = await _entorno.Eventos.ObtenerPendientesAsync(10, []);
        Assert.Single(todos);
    }

    public void Dispose() => _entorno.Dispose();
}
