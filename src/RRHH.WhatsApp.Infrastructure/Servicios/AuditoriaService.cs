using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Infrastructure.Servicios;

public sealed class AuditoriaService(RrhhDbContext db, TimeProvider reloj) : IAuditoriaService
{
    public async Task RegistrarAsync(
        string entidadTipo, string entidadId, int? analistaId,
        string accion, string? detalle, CancellationToken ct = default)
    {
        db.Auditorias.Add(new Auditoria
        {
            EntidadTipo = entidadTipo,
            EntidadId = entidadId,
            AnalistaId = analistaId,
            Accion = accion,
            Detalle = detalle is { Length: > 2000 } ? detalle[..2000] : detalle,
            Fecha = reloj.GetUtcNow().UtcDateTime
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Auditoria>> ListarPorEntidadAsync(
        string entidadTipo, string entidadId, CancellationToken ct = default) =>
        await db.Auditorias
            .AsNoTracking()
            .Where(a => a.EntidadTipo == entidadTipo && a.EntidadId == entidadId)
            .OrderByDescending(a => a.Fecha)
            .ToListAsync(ct);
}
