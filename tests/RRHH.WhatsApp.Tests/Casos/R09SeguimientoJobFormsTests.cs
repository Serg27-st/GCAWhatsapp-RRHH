using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Tests.Proveedores;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// Regla 9 — recordatorio de 24h y aviso al analista de 48h, sobre el barrido del Worker.
/// </summary>
public class R09SeguimientoJobFormsTests : IDisposable
{
    private readonly EntornoDeReglas _entorno = new();

    private static Dictionary<string, string> SinCabeceras() => [];

    private async Task<(int ConversacionId, int InvitacionId)> ConInvitacionAsync(TimeSpan antiguedad)
    {
        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);
        await _entorno.ConsumirOutboxAsync();

        var conversacionId = (await _entorno.Db.Conversaciones.FirstAsync()).ConversacionId;

        await _entorno.RefrescarActividadAsync(conversacionId);

        var invitacion = await _entorno.Invitaciones.CrearInvitacionAsync(conversacionId, hcId: 1);

        var guardada = await _entorno.Db.JobFormsInvitaciones
            .FirstAsync(i => i.InvitacionId == invitacion.InvitacionId);

        guardada.FechaEnvioLink = _entorno.Ahora - antiguedad;

        await _entorno.Db.SaveChangesAsync();

        return (conversacionId, invitacion.InvitacionId);
    }


    /// <summary>Deja la ventana de 24h cerrada, como cuando el postulante no escribe desde hace dias.</summary>
    private async Task CerrarVentanaAsync(int conversacionId)
    {
        var conversacion = await _entorno.Db.Conversaciones.FirstAsync(c => c.ConversacionId == conversacionId);
        conversacion.FechaUltimoMensajeEntrante = _entorno.Ahora.AddDays(-2);

        await _entorno.Db.SaveChangesAsync();
    }
    private async Task<JobFormsInvitacion> LeerInvitacionAsync(int invitacionId) =>
        await _entorno.Db.JobFormsInvitaciones.AsNoTracking()
            .FirstAsync(i => i.InvitacionId == invitacionId);

    /// <summary>
    /// Activa la plantilla solo dentro de la prueba. En produccion nacen inactivas hasta que Meta
    /// las apruebe; aca hace falta para poder verificar que el envio realmente sale.
    /// </summary>
    private async Task ActivarPlantillaAsync(string clave)
    {
        var plantilla = await _entorno.Db.Plantillas.FirstAsync(p => p.Clave == clave);
        plantilla.Activa = true;

        await _entorno.Db.SaveChangesAsync();
    }

    /// <summary>COR-03 (P1): con el hilo activo la ventana sigue abierta, asi que el recordatorio sale en texto.</summary>
    [Fact]
    public async Task A_las_24_horas_sale_el_recordatorio_al_postulante()
    {
        var (conversacionId, invitacionId) = await ConInvitacionAsync(TimeSpan.FromHours(25));

        await _entorno.Barrido.ProcesarConversacionAsync(conversacionId);
        await _entorno.DespacharAsync();

        var envio = Assert.Single(_entorno.Proveedor.Enviados, e => e.Detalle.Contains("aun no completas"));
        Assert.Equal("texto", envio.Tipo);

        Assert.True((await LeerInvitacionAsync(invitacionId)).RecordatorioEnviado);
    }

    /// <summary>Fuera de la ventana el mismo recordatorio necesita la plantilla aprobada (Regla 15).</summary>
    [Fact]
    public async Task Fuera_de_la_ventana_el_recordatorio_sale_como_plantilla()
    {
        await ActivarPlantillaAsync(ClavesPlantilla.RecordatorioJobForms);

        var (conversacionId, invitacionId) = await ConInvitacionAsync(TimeSpan.FromHours(25));
        await CerrarVentanaAsync(conversacionId);

        await _entorno.Barrido.ProcesarConversacionAsync(conversacionId);
        await _entorno.DespacharAsync();

        var envio = Assert.Single(_entorno.Proveedor.Enviados, e => e.Tipo == "plantilla");
        Assert.Equal(ClavesPlantilla.RecordatorioJobForms, envio.Detalle);

        Assert.True((await LeerInvitacionAsync(invitacionId)).RecordatorioEnviado);
    }

    [Fact]
    public async Task Antes_de_las_24_horas_no_pasa_nada()
    {
        var (conversacionId, invitacionId) = await ConInvitacionAsync(TimeSpan.FromHours(5));

        var acciones = await _entorno.Barrido.ProcesarConversacionAsync(conversacionId);

        Assert.Equal(0, acciones);
        Assert.False((await LeerInvitacionAsync(invitacionId)).RecordatorioEnviado);
    }

    [Fact]
    public async Task El_recordatorio_no_se_repite_en_el_siguiente_barrido()
    {
        var (conversacionId, _) = await ConInvitacionAsync(TimeSpan.FromHours(25));

        await _entorno.Barrido.ProcesarConversacionAsync(conversacionId);
        await _entorno.DespacharAsync();
        await _entorno.Barrido.ProcesarConversacionAsync(conversacionId);
        await _entorno.DespacharAsync();

        Assert.Single(_entorno.Proveedor.Enviados, e => e.Detalle.Contains("aun no completas"));
    }

    /// <summary>
    /// COR-03: sin ventana y sin plantilla aprobada el recordatorio no sale, y por eso tampoco se sella.
    /// Antes se sellaba igual y el postulante no lo recibia nunca: el barrido lo daba por enviado.
    /// </summary>
    [Fact]
    public async Task Sin_ventana_y_sin_plantilla_el_recordatorio_no_sale_ni_se_sella()
    {
        var (conversacionId, invitacionId) = await ConInvitacionAsync(TimeSpan.FromHours(25));
        await CerrarVentanaAsync(conversacionId);

        await _entorno.Barrido.ProcesarConversacionAsync(conversacionId);
        await _entorno.DespacharAsync();

        Assert.DoesNotContain(_entorno.Proveedor.Enviados, e => e.Detalle.Contains("aun no completas"));
        Assert.False((await LeerInvitacionAsync(invitacionId)).RecordatorioEnviado);

        // T1.14 (V32): la plantilla que falta aprobar queda como alerta, agrupada por plantilla.
        var alerta = await _entorno.Db.AlertasOperativas.SingleAsync();

        Assert.Equal(TiposAlerta.PlantillaNoAprobada, alerta.Tipo);
        Assert.StartsWith("plantilla:", alerta.Clave);
    }

    [Fact]
    public async Task A_las_48_horas_se_le_avisa_al_analista()
    {
        var (conversacionId, invitacionId) = await ConInvitacionAsync(TimeSpan.FromHours(49));

        await _entorno.Barrido.ProcesarConversacionAsync(conversacionId);
        await _entorno.DespacharAsync();

        var invitacion = await LeerInvitacionAsync(invitacionId);

        Assert.True(invitacion.AvisoAnalistaEnviado);
        Assert.NotNull(invitacion.FechaAvisoAnalista);

        // El aviso viaja por la outbox para que sobreviva si el analista no esta conectado.
        var aviso = await _entorno.Db.EventosSistema
            .AnyAsync(e => e.Tipo == TiposEvento.AnalistaNotificado);

        Assert.True(aviso);
    }

    [Fact]
    public async Task Una_invitacion_completada_ya_no_genera_seguimiento()
    {
        var (conversacionId, invitacionId) = await ConInvitacionAsync(TimeSpan.FromHours(72));

        await _entorno.Invitaciones.MarcarCompletadoAsync(invitacionId);

        var acciones = await _entorno.Barrido.ProcesarConversacionAsync(conversacionId);

        Assert.Equal(0, acciones);
    }

    [Fact]
    public async Task Reenviar_el_link_no_abre_una_segunda_invitacion()
    {
        // Dos filas para el mismo caso dispararian dos recordatorios y dos avisos al analista.
        var (conversacionId, invitacionId) = await ConInvitacionAsync(TimeSpan.FromHours(1));

        var repetida = await _entorno.Invitaciones.CrearInvitacionAsync(conversacionId, hcId: 1);

        Assert.Equal(invitacionId, repetida.InvitacionId);
        Assert.Single(_entorno.Db.JobFormsInvitaciones);
    }

    public void Dispose() => _entorno.Dispose();
}
