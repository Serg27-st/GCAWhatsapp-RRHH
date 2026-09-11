using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Infrastructure.Servicios;

/// <summary>
/// Seguimiento del link de JobForms (Regla 9). Es la tabla que permite saber quien recibio el
/// enlace y no lo completo, que es lo unico que hace posible el recordatorio de 24h y el aviso al
/// analista de 48h.
/// </summary>
public sealed class JobFormsInvitacionService(RrhhDbContext db, ILogger<JobFormsInvitacionService> log)
    : IJobFormsInvitacionService
{
    public async Task<JobFormsInvitacion> CrearInvitacionAsync(
        int conversacionId, int hcId, CancellationToken ct = default)
    {
        // Una invitacion viva para la misma vacante se reutiliza en vez de duplicarse: reenviar el
        // link no debe reiniciar el reloj del recordatorio ni crear una segunda fila que dispare
        // dos avisos al analista por el mismo caso.
        var vigente = await db.JobFormsInvitaciones
            .FirstOrDefaultAsync(i => i.ConversacionId == conversacionId && i.HcId == hcId && !i.Completado, ct);

        if (vigente is not null)
        {
            log.LogInformation(
                "Se reutiliza la invitacion {InvitacionId} para la conversacion {ConversacionId}.",
                vigente.InvitacionId, conversacionId);

            return vigente;
        }

        var conversacion = await db.Conversaciones
            .FirstAsync(c => c.ConversacionId == conversacionId, ct);

        var invitacion = new JobFormsInvitacion
        {
            ConversacionId = conversacionId,
            PostulanteId = conversacion.PostulanteId,
            HcId = hcId,
            Token = Guid.NewGuid(),
            FechaEnvioLink = DateTime.UtcNow
        };

        db.JobFormsInvitaciones.Add(invitacion);

        await db.SaveChangesAsync(ct);

        return invitacion;
    }

    public Task<JobFormsInvitacion?> ObtenerPorTokenAsync(Guid token, CancellationToken ct = default) =>
        db.JobFormsInvitaciones
            .Include(i => i.Hc)
            .FirstOrDefaultAsync(i => i.Token == token, ct);

    public Task MarcarRecordatorioEnviadoAsync(int invitacionId, CancellationToken ct = default) =>
        SellarAsync(invitacionId, i =>
        {
            i.RecordatorioEnviado = true;
            i.FechaRecordatorio = DateTime.UtcNow;
        }, ct);

    public Task MarcarAvisoAnalistaEnviadoAsync(int invitacionId, CancellationToken ct = default) =>
        SellarAsync(invitacionId, i =>
        {
            i.AvisoAnalistaEnviado = true;
            i.FechaAvisoAnalista = DateTime.UtcNow;
        }, ct);

    public Task MarcarCompletadoAsync(int invitacionId, CancellationToken ct = default) =>
        SellarAsync(invitacionId, i =>
        {
            i.Completado = true;
            i.FechaCompletado = DateTime.UtcNow;
        }, ct);

    public async Task<IReadOnlyList<JobFormsInvitacion>> ListarPendientesRecordatorioAsync(
        TimeSpan antiguedad, CancellationToken ct = default)
    {
        var limite = DateTime.UtcNow - antiguedad;

        return await db.JobFormsInvitaciones
            .AsNoTracking()
            .Include(i => i.Hc)
            .Where(i => !i.Completado && !i.RecordatorioEnviado && i.FechaEnvioLink <= limite)
            .OrderBy(i => i.FechaEnvioLink)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<JobFormsInvitacion>> ListarPendientesAvisoAnalistaAsync(
        TimeSpan antiguedad, CancellationToken ct = default)
    {
        var limite = DateTime.UtcNow - antiguedad;

        return await db.JobFormsInvitaciones
            .AsNoTracking()
            .Include(i => i.Hc)
            .Where(i => !i.Completado && !i.AvisoAnalistaEnviado && i.FechaEnvioLink <= limite)
            .OrderBy(i => i.FechaEnvioLink)
            .ToListAsync(ct);
    }

    private async Task SellarAsync(int invitacionId, Action<JobFormsInvitacion> cambio, CancellationToken ct)
    {
        var invitacion = await db.JobFormsInvitaciones
            .FirstOrDefaultAsync(i => i.InvitacionId == invitacionId, ct);

        if (invitacion is null)
        {
            log.LogWarning("No existe la invitacion {InvitacionId}; no hay nada que sellar.", invitacionId);
            return;
        }

        cambio(invitacion);

        await db.SaveChangesAsync(ct);
    }
}
