using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Tests.Proveedores;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// Regla 16 por postulación (FUN-11, A14, COR-14): lo que vence por silencio es el proceso, no el
/// hilo. Antes el estado <c>Archivada</c> existía pero nadie lo asignaba (M6), y el analista se
/// enteraba del archivado cuando la tarjeta ya no estaba.
/// </summary>
public class R16ArchivadoTests : IDisposable
{
    private readonly EntornoDeReglas _entorno = new();

    /// <summary>Un hilo con postulante y una postulación en el estado indicado, inactiva desde hace esos días.</summary>
    private async Task<int> PostulacionAsync(EstadoPostulacion estado, double diasSinActividad)
    {
        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);
        await _entorno.ConsumirOutboxAsync();

        var postulante = new Postulante { Dni = "45678912", FechaRegistro = _entorno.Ahora };
        _entorno.Db.Postulantes.Add(postulante);
        await _entorno.Db.SaveChangesAsync();

        var postulacion = new Postulacion
        {
            PostulanteId = postulante.PostulanteId,
            HcId = 1,
            CuentaId = EntornoDeReglas.CuentaId,
            AnalistaAsignadoId = EntornoDeReglas.TitularId,
            EtapaKanbanId = 1,
            Estado = estado,
            FechaCreacion = _entorno.Ahora.AddDays(-diasSinActividad),
            FechaUltimaActividad = _entorno.Ahora.AddDays(-diasSinActividad)
        };

        _entorno.Db.Postulaciones.Add(postulacion);

        var conversacion = await _entorno.Db.Conversaciones.FirstAsync();
        conversacion.PostulanteId = postulante.PostulanteId;

        await _entorno.Db.SaveChangesAsync();

        return postulacion.PostulacionId;
    }

    private Task<Postulacion> LeerAsync(int id) =>
        _entorno.Db.Postulaciones.AsNoTracking().FirstAsync(p => p.PostulacionId == id);

    private Task<int> AvisosDeArchivadoAsync() =>
        _entorno.Db.EventosSistema.CountAsync(
            e => e.Tipo == TiposEvento.AnalistaNotificado && e.Payload.Contains("archivara"));

    [Theory]
    [InlineData(EstadoPostulacion.EnProceso)]
    [InlineData(EstadoPostulacion.Descartado)]
    public async Task Tras_90_dias_sin_actividad_la_postulacion_se_archiva(EstadoPostulacion estado)
    {
        var id = await PostulacionAsync(estado, diasSinActividad: 91);

        await _entorno.Barrido.ProcesarPostulacionAsync(id);

        Assert.Equal(EstadoPostulacion.Archivada, (await LeerAsync(id)).Estado);
        Assert.Contains(_entorno.Db.Auditorias, a => a.Accion == "Archivado" && a.EntidadTipo == nameof(Postulacion));
    }

    /// <summary>Dossier, Regla 16: sin marca de contratado o reingreso. Esos dos no se archivan por silencio.</summary>
    [Theory]
    [InlineData(EstadoPostulacion.Contratado)]
    [InlineData(EstadoPostulacion.Reingreso)]
    public async Task Contratada_o_de_reingreso_no_se_archiva(EstadoPostulacion estado)
    {
        var id = await PostulacionAsync(estado, diasSinActividad: 200);

        await _entorno.Barrido.ProcesarPostulacionAsync(id);

        Assert.Equal(estado, (await LeerAsync(id)).Estado);
    }

    [Fact]
    public async Task Antes_del_plazo_no_se_archiva()
    {
        var id = await PostulacionAsync(EstadoPostulacion.EnProceso, diasSinActividad: 60);

        await _entorno.Barrido.ProcesarPostulacionAsync(id);

        Assert.Equal(EstadoPostulacion.EnProceso, (await LeerAsync(id)).Estado);
    }

    /// <summary>A14: el analista se entera antes, cuando todavía puede hacer algo, y una sola vez.</summary>
    [Fact]
    public async Task A_siete_dias_del_archivado_se_avisa_al_analista_una_sola_vez()
    {
        var id = await PostulacionAsync(EstadoPostulacion.EnProceso, diasSinActividad: 84);

        await _entorno.Barrido.ProcesarPostulacionAsync(id);
        await _entorno.Barrido.ProcesarPostulacionAsync(id);

        Assert.Equal(1, await AvisosDeArchivadoAsync());

        var postulacion = await LeerAsync(id);

        Assert.NotNull(postulacion.FechaAvisoArchivado);
        Assert.Equal(EstadoPostulacion.EnProceso, postulacion.Estado);
    }

    [Fact]
    public async Task Antes_de_la_ventana_de_aviso_no_se_avisa()
    {
        var id = await PostulacionAsync(EstadoPostulacion.EnProceso, diasSinActividad: 70);

        await _entorno.Barrido.ProcesarPostulacionAsync(id);

        Assert.Equal(0, await AvisosDeArchivadoAsync());
        Assert.Null((await LeerAsync(id)).FechaAvisoArchivado);
    }

    /// <summary>Una descartada ya no necesita que alguien la rescate: se archiva sin aviso previo.</summary>
    [Fact]
    public async Task Una_descartada_no_recibe_aviso_previo()
    {
        var id = await PostulacionAsync(EstadoPostulacion.Descartado, diasSinActividad: 84);

        await _entorno.Barrido.ProcesarPostulacionAsync(id);

        Assert.Equal(0, await AvisosDeArchivadoAsync());
    }

    public void Dispose() => _entorno.Dispose();
}
