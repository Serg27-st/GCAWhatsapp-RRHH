using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Tests.Proveedores;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// Reglas 7, 12 y 13 desde la bandeja: marcar, mover en el kanban y el cierre de cortesia que
/// dispara el descarte.
/// </summary>
public class AccionesBandejaTests : IDisposable
{
    private readonly EntornoDeReglas _entorno = new();

    /// <summary>Deja una postulacion en proceso, llegada por el circuito real del JobForms.</summary>
    private async Task<int> ConPostulacionAsync()
    {
        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);
        await _entorno.ConsumirOutboxAsync();

        var token = (await _entorno.Db.JobFormsInvitaciones.AsNoTracking().FirstAsync()).Token;

        var resultado = await _entorno.RecepcionFormulario.ProcesarAsync(new EnvioJobForms(
            token,
            new DatosPostulanteFormulario("45678912", "Maria Quispe", "+51987654321", null),
            "{}", null, ConsentimientoAceptado: true));

        await _entorno.ConsumirOutboxAsync();

        return resultado.PostulacionId;
    }

    private async Task ActivarCierreAsync()
    {
        var plantilla = await _entorno.Db.Plantillas
            .FirstAsync(p => p.Clave == ClavesPlantilla.CierreCortesia);

        plantilla.Activa = true;
        await _entorno.Db.SaveChangesAsync();
    }

    private async Task<int> EtapaAsync(string nombre) =>
        (await _entorno.Db.EtapasKanban.AsNoTracking().FirstAsync(e => e.Nombre == nombre)).EtapaId;

    [Fact]
    public async Task Regla_7_blacklist_exige_motivo()
    {
        await ConPostulacionAsync();

        var postulante = await _entorno.Db.Postulantes.AsNoTracking().FirstAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _entorno.Bandeja.MarcarAsync(
            postulante.PostulanteId, EntornoDeReglas.CuentaId,
            TipoEstadoPostulante.Blacklist, motivo: null, EntornoDeReglas.TitularId));
    }

    [Fact]
    public async Task Regla_7_whitelist_no_lo_exige_y_no_toca_la_postulacion()
    {
        var postulacionId = await ConPostulacionAsync();
        var postulante = await _entorno.Db.Postulantes.AsNoTracking().FirstAsync();

        await _entorno.Bandeja.MarcarAsync(
            postulante.PostulanteId, EntornoDeReglas.CuentaId,
            TipoEstadoPostulante.Whitelist, motivo: null, EntornoDeReglas.TitularId);

        var marca = await _entorno.Db.EstadosPostulanteCuenta.AsNoTracking().FirstAsync();
        Assert.Equal(TipoEstadoPostulante.Whitelist, marca.Tipo);

        var postulacion = await _entorno.Db.Postulaciones.AsNoTracking()
            .FirstAsync(p => p.PostulacionId == postulacionId);

        Assert.Equal(EstadoPostulacion.EnProceso, postulacion.Estado);
    }

    [Fact]
    public async Task Regla_7_blacklist_descarta_las_postulaciones_de_esa_cuenta()
    {
        // Dejarlas EnProceso contradiria la marca y las seguiria mostrando en el kanban.
        var postulacionId = await ConPostulacionAsync();
        var postulante = await _entorno.Db.Postulantes.AsNoTracking().FirstAsync();

        await _entorno.Bandeja.MarcarAsync(
            postulante.PostulanteId, EntornoDeReglas.CuentaId,
            TipoEstadoPostulante.Blacklist, "No cumple el perfil.", EntornoDeReglas.TitularId);

        var postulacion = await _entorno.Db.Postulaciones.AsNoTracking()
            .FirstAsync(p => p.PostulacionId == postulacionId);

        Assert.Equal(EstadoPostulacion.Descartado, postulacion.Estado);
        Assert.Equal(await EtapaAsync("Descartado"), postulacion.EtapaKanbanId);
    }

    [Fact]
    public async Task Regla_12_al_descartar_sale_el_cierre_de_cortesia()
    {
        await ActivarCierreAsync();
        await ConPostulacionAsync();

        var postulante = await _entorno.Db.Postulantes.AsNoTracking().FirstAsync();

        await _entorno.Bandeja.MarcarAsync(
            postulante.PostulanteId, EntornoDeReglas.CuentaId,
            TipoEstadoPostulante.Blacklist, "No cumple el perfil.", EntornoDeReglas.TitularId);

        // El mensaje no sale del endpoint: la regla lo decide y el Worker lo ejecuta.
        await _entorno.ConsumirOutboxAsync();

        Assert.Contains(_entorno.Proveedor.Enviados,
            e => e.Tipo == "plantilla" && e.Detalle == ClavesPlantilla.CierreCortesia);
    }

    [Fact]
    public async Task Regla_13_mover_a_una_columna_final_cierra_la_postulacion()
    {
        var postulacionId = await ConPostulacionAsync();

        await _entorno.Bandeja.MoverEtapaAsync(
            postulacionId, await EtapaAsync("Contratado"), EntornoDeReglas.TitularId);

        var postulacion = await _entorno.Db.Postulaciones.AsNoTracking()
            .FirstAsync(p => p.PostulacionId == postulacionId);

        Assert.Equal(EstadoPostulacion.Contratado, postulacion.Estado);
        Assert.NotNull(postulacion.FechaCambioEtapa);
    }

    [Fact]
    public async Task Regla_13_arrastrar_a_Descartado_tambien_dispara_el_cierre()
    {
        await ActivarCierreAsync();
        var postulacionId = await ConPostulacionAsync();

        await _entorno.Bandeja.MoverEtapaAsync(
            postulacionId, await EtapaAsync("Descartado"), EntornoDeReglas.TitularId);

        await _entorno.ConsumirOutboxAsync();

        Assert.Contains(_entorno.Proveedor.Enviados,
            e => e.Tipo == "plantilla" && e.Detalle == ClavesPlantilla.CierreCortesia);
    }

    [Fact]
    public async Task Contratar_no_dispara_ningun_cierre_de_cortesia()
    {
        await ActivarCierreAsync();
        var postulacionId = await ConPostulacionAsync();

        await _entorno.Bandeja.MoverEtapaAsync(
            postulacionId, await EtapaAsync("Contratado"), EntornoDeReglas.TitularId);

        await _entorno.ConsumirOutboxAsync();

        Assert.DoesNotContain(_entorno.Proveedor.Enviados,
            e => e.Tipo == "plantilla" && e.Detalle == ClavesPlantilla.CierreCortesia);
    }

    [Fact]
    public async Task Un_movimiento_intermedio_queda_en_la_auditoria()
    {
        var postulacionId = await ConPostulacionAsync();

        await _entorno.Bandeja.MoverEtapaAsync(
            postulacionId, await EtapaAsync("Entrevista"), EntornoDeReglas.TitularId);

        var registrado = await _entorno.Db.Auditorias
            .AnyAsync(a => a.Accion == "MovimientoKanban" && a.AnalistaId == EntornoDeReglas.TitularId);

        Assert.True(registrado);
    }

    public void Dispose() => _entorno.Dispose();
}
