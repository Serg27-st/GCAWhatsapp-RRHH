using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Api.Mapeo;
using RRHH.WhatsApp.Contracts.Bandeja;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Tests.Casos;

namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// E22 (FUN-13, M2): Meta acepta un mensaje y después avisa que no llegó.
/// <para>
/// Antes el acuse <c>failed</c> solo cambiaba una columna. El analista creía que había citado al
/// postulante a una entrevista, y el postulante nunca se enteró. Ahora quien lo mandó recibe un aviso
/// en vivo y el chat lo muestra.
/// </para>
/// </summary>
public class E22AcuseFallidoTests : IDisposable
{
    /// <summary>Meta: pasaron más de 24 horas desde el último mensaje del destinatario.</summary>
    private const int VentanaCerrada = 131047;

    /// <summary>Meta: no se pudo entregar (número sin WhatsApp, versión vieja, etc.).</summary>
    private const int NoEntregable = 131026;

    private readonly ArnesEscenario _arnes = new();

    /// <summary>El postulante eligió la empresa: el hilo es del titular y el bot ya le mandó el formulario.</summary>
    private Task AsignadaAsync() => _arnes.ConversarAsync(
        new Entrante("Hola"),
        new ConsumirOutbox(),
        new Boton("cuenta_7", "Alicorp"),
        new ConsumirOutbox(),
        new Despachar());

    private async Task<IReadOnlyList<string>> AvisosDeFalloAsync() =>
        [.. (await _arnes.Notificaciones()).Select(e => e.Payload).Where(p => p.Contains("no llego"))];

    private Task<Mensaje> UltimoSalienteAsync() =>
        _arnes.Entorno.Db.Mensajes.AsNoTracking()
            .Where(m => m.Direccion == DireccionMensaje.Saliente)
            .OrderByDescending(m => m.MensajeId)
            .FirstAsync();

    [Fact]
    public async Task Si_no_llega_la_respuesta_del_analista_se_le_avisa_y_el_chat_lo_muestra()
    {
        await AsignadaAsync();

        await _arnes.ConversarAsync(
            new RespuestaAnalista("Te esperamos el lunes a las 9.", EntornoDeReglas.TitularId),
            new Acuse("failed", NoEntregable, "Message undeliverable"));

        var aviso = Assert.Single(await AvisosDeFalloAsync());
        Assert.Contains($"\"analistaId\":{EntornoDeReglas.TitularId}", aviso);
        Assert.Contains("\"conversacionId\":", aviso);

        var mensaje = await UltimoSalienteAsync();
        Assert.Equal(EstadoEntrega.Fallido, mensaje.EstadoEntrega);

        // Lo que ve el analista en el chat: el estado, el motivo y la posibilidad de reintentarlo.
        var resumen = mensaje.AResumen();
        Assert.Equal(EstadosEntrega.Fallido, resumen.EstadoEntrega);
        Assert.Contains(NoEntregable.ToString(), resumen.Error);
        Assert.True(resumen.Reintentable);
    }

    /// <summary>El motivo va en palabras del analista, no con el código de Meta.</summary>
    [Fact]
    public async Task El_aviso_explica_la_ventana_cerrada()
    {
        await AsignadaAsync();

        await _arnes.ConversarAsync(
            new RespuestaAnalista("Te esperamos el lunes a las 9.", EntornoDeReglas.TitularId),
            new Acuse("failed", VentanaCerrada, "Re-engagement message"));

        var aviso = Assert.Single(await AvisosDeFalloAsync());
        Assert.Contains("24 horas", aviso);
        Assert.DoesNotContain(VentanaCerrada.ToString(), aviso);
    }

    /// <summary>P4: Meta reentrega el webhook y el analista no recibe el aviso dos veces.</summary>
    [Fact]
    public async Task Un_acuse_repetido_no_vuelve_a_avisar()
    {
        await AsignadaAsync();

        await _arnes.ConversarAsync(
            new RespuestaAnalista("Te esperamos el lunes a las 9.", EntornoDeReglas.TitularId),
            new Acuse("failed", NoEntregable, "Message undeliverable"),
            new Acuse("failed", NoEntregable, "Message undeliverable"));

        Assert.Single(await AvisosDeFalloAsync());
    }

    /// <summary>Lo que manda el bot no tiene autor: el aviso va a quien atiende el hilo.</summary>
    [Fact]
    public async Task Si_no_llega_un_mensaje_del_bot_se_avisa_a_quien_atiende()
    {
        await AsignadaAsync();

        var delBot = await UltimoSalienteAsync();
        Assert.Null(delBot.AnalistaId);

        await _arnes.ConversarAsync(new Acuse("failed", NoEntregable, "Message undeliverable"));

        var aviso = Assert.Single(await AvisosDeFalloAsync());
        Assert.Contains($"\"analistaId\":{EntornoDeReglas.TitularId}", aviso);

        // Reintentar es para lo que escribió una persona: el bot vuelve a decidir solo.
        Assert.False((await UltimoSalienteAsync()).AResumen().Reintentable);
    }

    /// <summary>Con el hilo todavía en el menú no hay nadie a quien avisar, y el acuse igual se aplica.</summary>
    [Fact]
    public async Task Sin_nadie_que_atienda_el_fallo_queda_registrado_sin_aviso()
    {
        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Despachar(),
            new Acuse("failed", NoEntregable, "Message undeliverable"));

        Assert.Empty(await AvisosDeFalloAsync());
        Assert.Equal(EstadoEntrega.Fallido, (await UltimoSalienteAsync()).EstadoEntrega);
    }

    [Fact]
    public async Task Un_acuse_de_entrega_o_lectura_no_avisa_a_nadie()
    {
        await AsignadaAsync();

        await _arnes.ConversarAsync(
            new RespuestaAnalista("Te esperamos el lunes a las 9.", EntornoDeReglas.TitularId),
            new Acuse("delivered"),
            new Acuse("read"));

        Assert.Empty(await AvisosDeFalloAsync());

        var resumen = (await UltimoSalienteAsync()).AResumen();
        Assert.Equal(EstadosEntrega.Leido, resumen.EstadoEntrega);
        Assert.Null(resumen.Error);
        Assert.False(resumen.Reintentable);
    }

    public void Dispose() => _arnes.Dispose();
}
