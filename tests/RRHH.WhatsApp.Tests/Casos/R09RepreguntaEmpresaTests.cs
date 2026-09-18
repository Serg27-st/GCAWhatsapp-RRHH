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
    /// Deja el hilo con cuenta ya elegida y un segundo mensaje entrante tras el hueco indicado. El
    /// hueco se produce moviendo el reloj (ARQ-12): la inactividad que mira la regla es la que el
    /// webhook fotografia antes de registrar el mensaje (C1, A13), no la distancia entre dos filas.
    /// </summary>
    private async Task<int> ConHuecoDeAsync(TimeSpan hueco)
    {
        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);
        await _entorno.ConsumirOutboxAsync();

        await _entorno.AvanzarAsync(hueco);

        await _entorno.Recepcion.ProcesarAsync(PayloadsDePrueba.MensajeDeTexto, SinCabeceras());

        var conversacionId = (await _entorno.Db.Conversaciones.FirstAsync()).ConversacionId;

        // El payload de prueba trae un timestamp fijo de 2025, que dejaria la ventana de 24h cerrada
        // y el menu sin poder salir. La instantanea del hueco ya viajo en el evento.
        await _entorno.RefrescarActividadAsync(conversacionId);

        return conversacionId;
    }

    [Fact]
    public async Task Tras_3_dias_el_bot_vuelve_a_preguntar_la_empresa()
    {
        var id = await ConHuecoDeAsync(TimeSpan.FromDays(10));

        await _entorno.ConsumirOutboxAsync();

        var conversacion = await _entorno.Db.Conversaciones.FirstAsync(c => c.ConversacionId == id);

        Assert.Null(conversacion.CuentaContextoId);
        Assert.Null(conversacion.AnalistaAtendiendoId);
        // V30: sin cuenta vuelve al menú del bot, no a «Sin clasificar»: el bot todavía la está atendiendo.
        Assert.Equal(EstadoConversacion.EnMenuBot, conversacion.Estado);

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
    public async Task Con_un_proceso_vivo_en_la_cuenta_no_se_repregunta()
    {
        // COR-07 (AL3): quien esta en proceso sigue con su analista. Limpiarle el contexto se lo
        // quitaba y mandaba el hilo a la bandeja general.
        var id = await ConHuecoDeAsync(TimeSpan.FromDays(10));

        var postulante = new Postulante { Dni = "45678912", FechaRegistro = _entorno.Ahora };
        _entorno.Db.Postulantes.Add(postulante);
        await _entorno.Db.SaveChangesAsync();

        _entorno.Db.Postulaciones.Add(new Postulacion
        {
            PostulanteId = postulante.PostulanteId,
            HcId = 1,
            CuentaId = EntornoDeReglas.CuentaId,
            EtapaKanbanId = 1,
            Estado = EstadoPostulacion.EnProceso,
            FechaCreacion = _entorno.Ahora,
            FechaUltimaActividad = _entorno.Ahora
        });

        var hilo = await _entorno.Db.Conversaciones.FirstAsync(c => c.ConversacionId == id);
        hilo.PostulanteId = postulante.PostulanteId;

        await _entorno.Db.SaveChangesAsync();

        await _entorno.ConsumirOutboxAsync();

        var conversacion = await _entorno.Db.Conversaciones.FirstAsync(c => c.ConversacionId == id);

        Assert.Equal(EntornoDeReglas.CuentaId, conversacion.CuentaContextoId);
        Assert.Equal(EntornoDeReglas.TitularId, conversacion.AnalistaAtendiendoId);
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
