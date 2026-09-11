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

        guardada.FechaEnvioLink = DateTime.UtcNow - antiguedad;

        await _entorno.Db.SaveChangesAsync();

        return (conversacionId, invitacion.InvitacionId);
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

    [Fact]
    public async Task A_las_24_horas_sale_el_recordatorio_al_postulante()
    {
        await ActivarPlantillaAsync(ClavesPlantilla.RecordatorioJobForms);

        var (conversacionId, invitacionId) = await ConInvitacionAsync(TimeSpan.FromHours(25));

        await _entorno.Barrido.ProcesarConversacionAsync(conversacionId);

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
        await ActivarPlantillaAsync(ClavesPlantilla.RecordatorioJobForms);

        var (conversacionId, _) = await ConInvitacionAsync(TimeSpan.FromHours(25));

        await _entorno.Barrido.ProcesarConversacionAsync(conversacionId);
        await _entorno.Barrido.ProcesarConversacionAsync(conversacionId);

        Assert.Single(_entorno.Proveedor.Enviados, e => e.Tipo == "plantilla");
    }

    [Fact]
    public async Task El_recordatorio_se_sella_aunque_la_plantilla_siga_sin_aprobar()
    {
        // Con la plantilla inactiva el envio se omite, pero el sello igual se pone: reintentarlo
        // en cada barrido convertiria un tramite pendiente en una tanda de mensajes repetidos.
        var (conversacionId, invitacionId) = await ConInvitacionAsync(TimeSpan.FromHours(25));

        await _entorno.Barrido.ProcesarConversacionAsync(conversacionId);

        Assert.DoesNotContain(_entorno.Proveedor.Enviados, e => e.Tipo == "plantilla");
        Assert.True((await LeerInvitacionAsync(invitacionId)).RecordatorioEnviado);

        var omitido = await _entorno.Db.EventosSistema
            .AnyAsync(e => e.Tipo == TiposEvento.EnvioOmitidoSinPlantilla);

        Assert.True(omitido);
    }

    [Fact]
    public async Task A_las_48_horas_se_le_avisa_al_analista()
    {
        var (conversacionId, invitacionId) = await ConInvitacionAsync(TimeSpan.FromHours(49));

        await _entorno.Barrido.ProcesarConversacionAsync(conversacionId);

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
