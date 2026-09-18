using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Tests.Casos;

namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// E06 (FUN-06, A12, P3): el postulante escribe algo que el bot no entiende y despues se calla. Sin
/// plazo, ese hilo se quedaba en el menú del bot para siempre: ni el bot lo resolvía ni ningún
/// analista lo veía.
/// </summary>
public class E06SilencioTests : IDisposable
{
    private const int JefaturaId = 13;

    private readonly ArnesEscenario _arnes = new();

    private async Task ConJefaturaAsync()
    {
        _arnes.Entorno.Db.Analistas.Add(new Analista
        {
            AnalistaId = JefaturaId,
            Nombre = "Rosa Diaz",
            Email = "rosa@gca.pe",
            Rol = RolAnalista.Jefatura,
            Activo = true
        });

        await _arnes.Entorno.Db.SaveChangesAsync();
    }

    /// <summary>Un texto que el bot no reconoce, con el sello desde el que corre el plazo.</summary>
    private async Task ConTextoNoReconocidoAsync()
    {
        await _arnes.ConHorarioComercialAsync();
        await ConJefaturaAsync();

        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Despachar(),
            new Entrante("necesito trabajo urgente"),
            new ConsumirOutbox(),
            new Despachar());

        Assert.NotNull((await _arnes.ConversacionAsync()).FechaTextoNoReconocido);
    }

    [Fact]
    public async Task Tras_el_plazo_de_silencio_el_hilo_pasa_a_Sin_clasificar()
    {
        await ConTextoNoReconocidoAsync();

        // Dos horas hábiles sin que el postulante vuelva a escribir (menu.horas_derivacion).
        await _arnes.ConversarAsync(new Avanzar(TimeSpan.FromHours(2)), new Barrido());

        var conversacion = await _arnes.ConversacionAsync();

        Assert.Equal(EstadoConversacion.PendienteClasificar, conversacion.Estado);
        Assert.NotNull(conversacion.FechaPendienteDesde);
        Assert.Contains(_arnes.Entorno.Db.Auditorias, a => a.Accion == "DerivadaABandejaGeneral");
    }

    [Fact]
    public async Task Antes_del_plazo_sigue_en_el_menu_del_bot()
    {
        await ConTextoNoReconocidoAsync();

        await _arnes.ConversarAsync(new Avanzar(TimeSpan.FromMinutes(45)), new Barrido());

        Assert.Equal(EstadoConversacion.EnMenuBot, await _arnes.EstadoConversacion());
    }

    /// <summary>P3: ningún hilo sin dueño ni plazo. A las 2 h hábiles en la bandeja general, avisa.</summary>
    [Fact]
    public async Task En_Sin_clasificar_se_le_avisa_a_Jefatura_pasado_el_plazo()
    {
        await ConTextoNoReconocidoAsync();

        await _arnes.ConversarAsync(new Avanzar(TimeSpan.FromHours(2)), new Barrido());

        var avisosAntes = (await _arnes.Notificaciones()).Count;

        // Otras dos horas hábiles en la bandeja general, sin que nadie la tome.
        await _arnes.ConversarAsync(new Avanzar(TimeSpan.FromHours(2)), new Barrido());

        var nuevos = (await _arnes.Notificaciones()).Skip(avisosAntes).ToList();

        Assert.Contains(nuevos, e => e.Payload.Contains($"\"analistaId\":{JefaturaId}"));
        Assert.NotNull((await _arnes.ConversacionAsync()).FechaAvisoPendiente);
    }

    [Fact]
    public async Task El_aviso_de_Sin_clasificar_no_se_repite()
    {
        await ConTextoNoReconocidoAsync();

        await _arnes.ConversarAsync(
            new Avanzar(TimeSpan.FromHours(2)), new Barrido(),
            new Avanzar(TimeSpan.FromHours(2)), new Barrido());

        var despuesDelPrimero = (await _arnes.Notificaciones()).Count;

        await _arnes.ConversarAsync(new Avanzar(TimeSpan.FromHours(2)), new Barrido());

        Assert.Equal(despuesDelPrimero, (await _arnes.Notificaciones()).Count);
    }

    /// <summary>Si el postulante vuelve y elige, no hay nada que derivar: el bot lo resolvió.</summary>
    [Fact]
    public async Task Si_el_postulante_elige_antes_del_plazo_no_se_deriva()
    {
        await ConTextoNoReconocidoAsync();

        await _arnes.ConversarAsync(
            new Avanzar(TimeSpan.FromMinutes(30)),
            new Boton("cuenta_7", "Alicorp"),
            new ConsumirOutbox(),
            new Despachar(),
            new Avanzar(TimeSpan.FromHours(3)),
            new Barrido());

        var conversacion = await _arnes.ConversacionAsync();

        // Puede haber escalado al respaldo por la Regla 2 —el analista no respondio en 3 horas—, que
        // es otra cosa: lo que importa es que no cayo en la bandeja general.
        Assert.NotEqual(EstadoConversacion.PendienteClasificar, conversacion.Estado);
        Assert.Null(conversacion.FechaTextoNoReconocido);
    }

    /// <summary>Tomarla también apaga el plazo: ya tiene dueño (FUN-01).</summary>
    [Fact]
    public async Task Tomarla_apaga_el_plazo_de_la_bandeja_general()
    {
        await ConTextoNoReconocidoAsync();

        await _arnes.ConversarAsync(new Avanzar(TimeSpan.FromHours(2)), new Barrido());

        var id = (await _arnes.ConversacionAsync()).ConversacionId;

        await _arnes.Entorno.Conversaciones.TomarAsync(id, EntornoDeReglas.TitularId, EntornoDeReglas.CuentaId);

        await _arnes.ConversarAsync(new Avanzar(TimeSpan.FromHours(3)), new Barrido());

        // Puede haber otros avisos —el escalamiento de la Regla 2, por ejemplo—, pero ninguno a
        // Jefatura: el hilo ya tiene dueño y su plazo dejo de correr.
        Assert.DoesNotContain(
            await _arnes.Notificaciones(),
            e => e.Payload.Contains($"\"analistaId\":{JefaturaId}"));

        Assert.Null((await _arnes.ConversacionAsync()).FechaAvisoPendiente);
    }

    public void Dispose() => _arnes.Dispose();
}
