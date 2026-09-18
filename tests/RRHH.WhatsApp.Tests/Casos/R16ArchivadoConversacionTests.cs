using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Tests.Proveedores;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// Regla 16 por conversación (FUN-11): el hilo se archiva cuando ya no queda ningún proceso que lo
/// sostenga. Comparte disparador con la Regla 2, así que las pruebas cubren también cómo conviven:
/// un hilo muerto se archiva en vez de escalarse.
/// </summary>
public class R16ArchivadoConversacionTests : IDisposable
{
    private readonly EntornoDeReglas _entorno = new();

    private async Task<int> ConversacionAsignadaAsync()
    {
        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);
        await _entorno.ConsumirOutboxAsync();

        return (await _entorno.Db.Conversaciones.FirstAsync()).ConversacionId;
    }

    private async Task InactivaDesdeHaceAsync(int conversacionId, TimeSpan atraso)
    {
        var conversacion = await _entorno.Db.Conversaciones.FirstAsync(c => c.ConversacionId == conversacionId);

        var momento = _entorno.Ahora - atraso;

        conversacion.FechaUltimaActividad = momento;
        conversacion.FechaUltimoMensajeEntrante = momento;

        await _entorno.Db.SaveChangesAsync();
    }

    /// <summary>Le da al hilo un postulante con una postulación en el estado indicado.</summary>
    private async Task ConPostulacionAsync(int conversacionId, EstadoPostulacion estado)
    {
        var postulante = new Postulante { Dni = "45678912", FechaRegistro = _entorno.Ahora };
        _entorno.Db.Postulantes.Add(postulante);
        await _entorno.Db.SaveChangesAsync();

        _entorno.Db.Postulaciones.Add(new Postulacion
        {
            PostulanteId = postulante.PostulanteId,
            HcId = 1,
            CuentaId = EntornoDeReglas.CuentaId,
            EtapaKanbanId = 1,
            Estado = estado,
            FechaCreacion = _entorno.Ahora,
            FechaUltimaActividad = _entorno.Ahora
        });

        var conversacion = await _entorno.Db.Conversaciones.FirstAsync(c => c.ConversacionId == conversacionId);
        conversacion.PostulanteId = postulante.PostulanteId;

        await _entorno.Db.SaveChangesAsync();
    }

    private Task<Conversacion> LeerAsync(int id) =>
        _entorno.Db.Conversaciones.AsNoTracking().FirstAsync(c => c.ConversacionId == id);

    [Fact]
    public async Task Tras_90_dias_sin_actividad_el_hilo_se_archiva()
    {
        var id = await ConversacionAsignadaAsync();

        await InactivaDesdeHaceAsync(id, TimeSpan.FromDays(120));

        var acciones = await _entorno.Barrido.ProcesarConversacionAsync(id);

        Assert.True(acciones > 0);
        Assert.Equal(EstadoConversacion.Archivada, (await LeerAsync(id)).Estado);
        Assert.Contains(_entorno.Db.Auditorias, a => a.Accion == "Archivado" && a.EntidadTipo == nameof(Conversacion));
    }

    [Fact]
    public async Task Antes_del_plazo_no_se_archiva()
    {
        var id = await ConversacionAsignadaAsync();

        await InactivaDesdeHaceAsync(id, TimeSpan.FromDays(30));

        await _entorno.Barrido.ProcesarConversacionAsync(id);

        Assert.NotEqual(EstadoConversacion.Archivada, (await LeerAsync(id)).Estado);
    }

    /// <summary>Dossier, Regla 16: sin marca de contratado o reingreso, y sin procesos en curso.</summary>
    [Theory]
    [InlineData(EstadoPostulacion.EnProceso)]
    [InlineData(EstadoPostulacion.Reingreso)]
    [InlineData(EstadoPostulacion.Contratado)]
    public async Task Un_proceso_vivo_o_contratado_no_deja_archivar_el_hilo(EstadoPostulacion estado)
    {
        var id = await ConversacionAsignadaAsync();

        await ConPostulacionAsync(id, estado);
        await InactivaDesdeHaceAsync(id, TimeSpan.FromDays(120));

        await _entorno.Barrido.ProcesarConversacionAsync(id);

        Assert.NotEqual(EstadoConversacion.Archivada, (await LeerAsync(id)).Estado);
    }

    [Fact]
    public async Task Un_postulante_descartado_si_deja_archivar_el_hilo()
    {
        var id = await ConversacionAsignadaAsync();

        await ConPostulacionAsync(id, EstadoPostulacion.Descartado);
        await InactivaDesdeHaceAsync(id, TimeSpan.FromDays(120));

        await _entorno.Barrido.ProcesarConversacionAsync(id);

        Assert.Equal(EstadoConversacion.Archivada, (await LeerAsync(id)).Estado);
    }

    [Fact]
    public async Task Un_hilo_muerto_se_archiva_en_vez_de_escalarse()
    {
        // Las dos reglas corren sobre el mismo disparador y ambas calzan: sin la prioridad de la
        // 16 sobre la 2, el respaldo recibiria una conversacion de hace tres meses.
        var id = await ConversacionAsignadaAsync();

        await InactivaDesdeHaceAsync(id, TimeSpan.FromDays(120));

        await _entorno.Barrido.ProcesarConversacionAsync(id);

        var conversacion = await LeerAsync(id);

        Assert.Equal(EstadoConversacion.Archivada, conversacion.Estado);
        Assert.Equal(EntornoDeReglas.TitularId, conversacion.AnalistaAtendiendoId);
    }

    public void Dispose() => _entorno.Dispose();
}
