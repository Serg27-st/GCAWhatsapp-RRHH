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

    public async Task AnonimizarDetallesAsync(
        IReadOnlyCollection<int> conversacionIds, int postulanteId, CancellationToken ct = default)
    {
        var hilos = conversacionIds.Select(i => i.ToString()).ToList();
        var persona = postulanteId.ToString();

        var registros = await db.Auditorias
            .Where(a => (a.EntidadTipo == nameof(Conversacion) && hilos.Contains(a.EntidadId))
                     || (a.EntidadTipo == nameof(Postulante) && a.EntidadId == persona))
            .ToListAsync(ct);

        // Se vacia el detalle y no la fila: que paso y cuando es lo que sostiene la trazabilidad de la
        // Regla 4. Lo que puede tener nombre, telefono o lo que escribio la persona es el detalle.
        foreach (var registro in registros)
            registro.Detalle = null;

        if (registros.Count > 0)
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
