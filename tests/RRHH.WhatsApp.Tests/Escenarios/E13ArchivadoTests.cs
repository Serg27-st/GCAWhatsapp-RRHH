using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Tests.Casos;

namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// E13 (FUN-11, A14, AL10): el ciclo completo del silencio. Aviso previo al analista, archivado, y
/// qué pasa cuando la persona vuelve a escribir.
/// <para>
/// Antes, un hilo archivado que recibía un mensaje quedaba invisible: seguía <c>Archivada</c>, ninguna
/// bandeja lo mostraba y el postulante no recibía respuesta de nadie.
/// </para>
/// </summary>
public class E13ArchivadoTests : IDisposable
{
    private readonly ArnesEscenario _arnes = new();

    private Task<Domain.Entidades.Postulacion> PostulacionAsync() =>
        _arnes.Entorno.Db.Postulaciones.AsNoTracking().FirstAsync();

    private async Task<IReadOnlyList<string>> AvisosDeArchivadoAsync() =>
        [.. (await _arnes.Notificaciones()).Select(e => e.Payload).Where(p => p.Contains("archivara"))];

    /// <summary>Deja una postulación en curso, con su hilo asignado y respondido por el analista.</summary>
    private async Task EnProcesoAsync()
    {
        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Boton("cuenta_7", "Alicorp"),
            new ConsumirOutbox(),
            new Despachar(),
            new Formulario(),
            new ConsumirOutbox(),
            new Despachar(),
            // El analista contesta: sin esto el barrido escalaría el hilo, que es otra historia (E08).
            new RespuestaAnalista("Recibimos tu ficha, te escribimos pronto.", EntornoDeReglas.TitularId));
    }

    [Fact]
    public async Task A_los_83_dias_se_avisa_y_a_los_90_se_archiva()
    {
        await EnProcesoAsync();

        await _arnes.ConversarAsync(new Avanzar(TimeSpan.FromDays(83)), new Barrido());

        var aviso = Assert.Single(await AvisosDeArchivadoAsync());
        Assert.Contains($"\"analistaId\":{EntornoDeReglas.TitularId}", aviso);
        Assert.Equal(EstadoPostulacion.EnProceso, (await PostulacionAsync()).Estado);

        await _arnes.ConversarAsync(new Avanzar(TimeSpan.FromDays(8)), new Barrido());

        Assert.Equal(EstadoPostulacion.Archivada, (await PostulacionAsync()).Estado);

        // El hilo se archiva en la vuelta siguiente, cuando ya no le queda ningún proceso vivo.
        await _arnes.ConversarAsync(new Barrido());

        Assert.Equal(EstadoConversacion.Archivada, await _arnes.EstadoConversacion());
        Assert.Single(await AvisosDeArchivadoAsync());
    }

    /// <summary>AL10: vuelve a escribir después del archivado y el bot le ofrece empezar de nuevo.</summary>
    [Fact]
    public async Task Si_vuelve_a_escribir_despues_del_archivado_recibe_el_menu()
    {
        await EnProcesoAsync();

        await _arnes.ConversarAsync(
            new Avanzar(TimeSpan.FromDays(91)), new Barrido(), new Barrido());

        Assert.Equal(EstadoConversacion.Archivada, await _arnes.EstadoConversacion());

        var menusAntes = _arnes.Enviados().Count(e => e.Detalle.Contains("cuenta_"));

        await _arnes.ConversarAsync(
            new Entrante("Hola, sigo buscando trabajo"),
            new ConsumirOutbox(),
            new Despachar());

        var conversacion = await _arnes.ConversacionAsync();

        Assert.Equal(EstadoConversacion.EnMenuBot, conversacion.Estado);
        Assert.Null(conversacion.CuentaContextoId);
        Assert.Equal(menusAntes + 1, _arnes.Enviados().Count(e => e.Detalle.Contains("cuenta_")));

        // El menú la saluda como a alguien que vuelve, no como a quien escribe por primera vez.
        var ultimo = await _arnes.Entorno.Db.Mensajes.AsNoTracking()
            .Where(m => m.Direccion == DireccionMensaje.Saliente)
            .OrderByDescending(m => m.MensajeId)
            .FirstAsync();

        Assert.StartsWith("Hola de nuevo", ultimo.Contenido);
    }

    /// <summary>
    /// FUN-11 con A2: si el analista marcó el reingreso mientras el hilo estaba archivado, la persona
    /// vuelve directo con su analista, sin menú.
    /// </summary>
    [Fact]
    public async Task Con_un_reingreso_vuelve_activa_con_su_analista()
    {
        await EnProcesoAsync();

        await _arnes.ConversarAsync(
            new Avanzar(TimeSpan.FromDays(91)), new Barrido(), new Barrido());

        var postulacionId = (await PostulacionAsync()).PostulacionId;
        await _arnes.Entorno.Postulaciones.MarcarReingresoAsync(postulacionId, EntornoDeReglas.TitularId);

        var menusAntes = _arnes.Enviados().Count(e => e.Detalle.Contains("cuenta_"));

        await _arnes.ConversarAsync(
            new Entrante("Hola, me llamaron para volver"),
            new ConsumirOutbox(),
            new Despachar());

        var conversacion = await _arnes.ConversacionAsync();

        Assert.Equal(EstadoConversacion.Activa, conversacion.Estado);
        Assert.Equal(EntornoDeReglas.CuentaId, conversacion.CuentaContextoId);
        Assert.Equal(EntornoDeReglas.TitularId, conversacion.AnalistaAtendiendoId);
        Assert.Equal(menusAntes, _arnes.Enviados().Count(e => e.Detalle.Contains("cuenta_")));
    }

    public void Dispose() => _arnes.Dispose();
}
