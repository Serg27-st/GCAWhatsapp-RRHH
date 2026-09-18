using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Tests.Casos;

namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// FUN-08 (A2): un ex trabajador que vuelve, o un descarte que el analista reconsidera, es un proceso
/// vivo otra vez. Antes no había dónde registrarlo: el hilo volvía al menú de empresas como si fuera
/// un desconocido, y la Regla 16 lo archivaba a los 90 días.
/// </summary>
public class E13ReingresoTests : IDisposable
{
    private readonly ArnesEscenario _arnes = new();

    private Task<Domain.Entidades.Postulacion> PostulacionAsync() =>
        _arnes.Entorno.Db.Postulaciones.AsNoTracking().FirstAsync();

    /// <summary>Deja una postulación descartada, con su hilo ya identificado.</summary>
    private async Task<int> DescartadaAsync()
    {
        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Boton("cuenta_7", "Alicorp"),
            new ConsumirOutbox(),
            new Despachar(),
            new Formulario(),
            new ConsumirOutbox(),
            new Despachar());

        var postulacionId = (await PostulacionAsync()).PostulacionId;

        var descartado = await _arnes.Entorno.Db.EtapasKanban.AsNoTracking()
            .FirstAsync(e => e.EstadoResultante == EstadoPostulacion.Descartado);

        await _arnes.Entorno.Bandeja.MoverEtapaAsync(postulacionId, descartado.EtapaId, EntornoDeReglas.TitularId);
        await _arnes.Entorno.ConsumirOutboxAsync();

        return postulacionId;
    }

    [Fact]
    public async Task Marcar_reingreso_devuelve_la_postulacion_al_tablero()
    {
        var postulacionId = await DescartadaAsync();

        await _arnes.Entorno.Postulaciones.MarcarReingresoAsync(postulacionId, EntornoDeReglas.TitularId);

        var postulacion = await PostulacionAsync();
        var etapa = await _arnes.Entorno.Db.EtapasKanban.AsNoTracking()
            .FirstAsync(e => e.EtapaId == postulacion.EtapaKanbanId);

        Assert.Equal(EstadoPostulacion.Reingreso, postulacion.Estado);
        Assert.NotNull(postulacion.FechaReingreso);
        Assert.False(etapa.EsFinal);
        Assert.False(postulacion.CierreCortesiaPendiente);
        Assert.Contains(_arnes.Entorno.Db.Auditorias, a => a.Accion == "Reingreso");
    }

    /// <summary>
    /// El caso completo: vuelve a escribir a los diez días y el bot lo manda directo con su analista,
    /// sin menú. Sin el reingreso, la Regla 9 le habría limpiado el contexto por inactividad.
    /// </summary>
    [Fact]
    public async Task Quien_reingresa_vuelve_directo_con_su_analista()
    {
        var postulacionId = await DescartadaAsync();

        await _arnes.Entorno.Postulaciones.MarcarReingresoAsync(postulacionId, EntornoDeReglas.TitularId);

        var menusAntes = _arnes.Enviados().Count(e => e.Detalle.Contains("cuenta_"));

        await _arnes.ConversarAsync(
            new Avanzar(TimeSpan.FromDays(10)),
            new Entrante("Hola, me dijeron que vuelva"),
            new ConsumirOutbox(),
            new Despachar());

        var conversacion = await _arnes.ConversacionAsync();

        Assert.Equal(EstadoConversacion.Activa, conversacion.Estado);
        Assert.Equal(EntornoDeReglas.CuentaId, conversacion.CuentaContextoId);
        Assert.Equal(EntornoDeReglas.TitularId, conversacion.AnalistaAtendiendoId);
        Assert.Equal(menusAntes, _arnes.Enviados().Count(e => e.Detalle.Contains("cuenta_")));
    }


    /// <summary>
    /// FUN-08: aunque el hilo haya perdido el contexto —la Regla 9 lo limpia tras varios días—, con un
    /// único proceso vivo no hay nada que preguntar: se retoma ese y su analista, sin menú.
    /// </summary>
    [Fact]
    public async Task Sin_contexto_y_con_un_solo_proceso_vivo_se_retoma_sin_menu()
    {
        var postulacionId = await DescartadaAsync();

        await _arnes.Entorno.Postulaciones.MarcarReingresoAsync(postulacionId, EntornoDeReglas.TitularId);

        var conversacion = await _arnes.ConversacionAsync();

        // Lo que deja la Regla 9 cuando el postulante reaparece tras varios días.
        await _arnes.Entorno.Conversaciones.LimpiarCuentaContextoAsync(
            conversacion.ConversacionId, "Prueba: el bot vuelve a preguntar la empresa.");

        var menusAntes = _arnes.Enviados().Count(e => e.Detalle.Contains("cuenta_"));

        await _arnes.ConversarAsync(
            new Entrante("Hola de nuevo"),
            new ConsumirOutbox(),
            new Despachar());

        var despues = await _arnes.ConversacionAsync();

        Assert.Equal(EntornoDeReglas.CuentaId, despues.CuentaContextoId);
        Assert.Equal(EntornoDeReglas.TitularId, despues.AnalistaAtendiendoId);
        Assert.Equal(menusAntes, _arnes.Enviados().Count(e => e.Detalle.Contains("cuenta_")));
    }
    /// <summary>A2: un reingreso es proceso vivo, así que la Regla 16 no lo archiva.</summary>
    [Fact]
    public async Task Un_reingreso_no_se_archiva_por_inactividad()
    {
        var postulacionId = await DescartadaAsync();

        await _arnes.Entorno.Postulaciones.MarcarReingresoAsync(postulacionId, EntornoDeReglas.TitularId);

        await _arnes.ConversarAsync(new Avanzar(TimeSpan.FromDays(120)), new Barrido());

        Assert.NotEqual(EstadoConversacion.Archivada, await _arnes.EstadoConversacion());
    }

    public void Dispose() => _arnes.Dispose();
}
