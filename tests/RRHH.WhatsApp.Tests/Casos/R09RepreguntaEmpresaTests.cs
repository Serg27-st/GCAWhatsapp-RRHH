using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Tests.Proveedores;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// Regla 9 — repregunta de empresa. Se prueba sobre el circuito real porque lo que importa es la
/// interaccion: la repregunta tiene que ganarle a la Regla 1, que si no volveria a enrutar la
/// conversacion a la cuenta vieja.
/// </summary>
public class R09RepreguntaEmpresaTests : IDisposable
{
    private readonly EntornoDeReglas _entorno = new();

    private static Dictionary<string, string> SinCabeceras() => [];

    /// <summary>
    /// Deja el hilo con cuenta ya elegida y un segundo mensaje entrante separado del primero por
    /// el hueco indicado. Las fechas se fijan a mano porque los payloads de prueba traen un
    /// timestamp fijo.
    /// </summary>
    private async Task<int> ConHuecoDeAsync(TimeSpan hueco)
    {
        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);
        await _entorno.ConsumirOutboxAsync();

        await _entorno.IngresarAsync(PayloadsDePrueba.MensajeDeTexto);

        var mensajes = await _entorno.Db.Mensajes
            .Where(m => m.Direccion == DireccionMensaje.Entrante)
            .OrderBy(m => m.MensajeId)
            .ToListAsync();

        Assert.Equal(2, mensajes.Count);

        var ahora = _entorno.Ahora;

        mensajes[0].FechaEnvio = ahora - hueco;
        mensajes[1].FechaEnvio = ahora;

        await _entorno.Db.SaveChangesAsync();

        return (await _entorno.Db.Conversaciones.FirstAsync()).ConversacionId;
    }

    [Fact]
    public async Task Tras_3_dias_el_bot_vuelve_a_preguntar_la_empresa()
    {
        var id = await ConHuecoDeAsync(TimeSpan.FromDays(10));

        await _entorno.ConsumirOutboxAsync();

        var conversacion = await _entorno.Db.Conversaciones.FirstAsync(c => c.ConversacionId == id);

        Assert.Null(conversacion.CuentaContextoId);
        Assert.Null(conversacion.AnalistaAtendiendoId);
        Assert.Equal(EstadoConversacion.PendienteClasificar, conversacion.Estado);

        var menu = Assert.Single(_entorno.Proveedor.Enviados, e => e.Tipo == "botones");
        Assert.Equal(IdsBoton.ParaCuenta(EntornoDeReglas.CuentaId), menu.Detalle);
    }

    [Fact]
    public async Task Antes_del_plazo_la_conversacion_sigue_con_su_cuenta_y_su_analista()
    {
        var id = await ConHuecoDeAsync(TimeSpan.FromHours(6));

        await _entorno.ConsumirOutboxAsync();

        var conversacion = await _entorno.Db.Conversaciones.FirstAsync(c => c.ConversacionId == id);

        Assert.Equal(EntornoDeReglas.CuentaId, conversacion.CuentaContextoId);
        Assert.Equal(EntornoDeReglas.TitularId, conversacion.AnalistaAtendiendoId);
    }

    [Fact]
    public async Task Si_el_analista_ya_decidio_no_se_repregunta()
    {
        // El mini-mantenimiento manda: volver a ofrecerle el menu a alguien ya contratado
        // contradice la decision que el analista acaba de tomar.
        var id = await ConHuecoDeAsync(TimeSpan.FromDays(10));

        var postulante = new Postulante { Dni = "45678912", FechaRegistro = _entorno.Ahora };
        _entorno.Db.Postulantes.Add(postulante);
        await _entorno.Db.SaveChangesAsync();

        _entorno.Db.Postulaciones.Add(new Postulacion
        {
            PostulanteId = postulante.PostulanteId,
            HcId = 1,
            CuentaId = EntornoDeReglas.CuentaId,
            EtapaKanbanId = 4,
            Estado = EstadoPostulacion.Contratado,
            FechaCreacion = _entorno.Ahora,
            FechaUltimaActividad = _entorno.Ahora
        });

        var hilo = await _entorno.Db.Conversaciones.FirstAsync(c => c.ConversacionId == id);
        hilo.PostulanteId = postulante.PostulanteId;

        await _entorno.Db.SaveChangesAsync();

        await _entorno.ConsumirOutboxAsync();

        var conversacion = await _entorno.Db.Conversaciones.FirstAsync(c => c.ConversacionId == id);

        Assert.Equal(EntornoDeReglas.CuentaId, conversacion.CuentaContextoId);
    }

    [Fact]
    public async Task El_primer_mensaje_del_hilo_no_dispara_la_repregunta()
    {
        // Sin un mensaje anterior no hay hueco que medir; la Regla 1 debe enrutar con normalidad.
        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);
        await _entorno.ConsumirOutboxAsync();

        var conversacion = await _entorno.Db.Conversaciones.FirstAsync();

        Assert.Equal(EntornoDeReglas.CuentaId, conversacion.CuentaContextoId);
        Assert.Equal(EntornoDeReglas.TitularId, conversacion.AnalistaAtendiendoId);
    }

    public void Dispose() => _entorno.Dispose();
}
