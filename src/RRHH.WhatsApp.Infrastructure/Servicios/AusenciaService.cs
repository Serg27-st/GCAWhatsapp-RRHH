using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Infrastructure.Servicios;

/// <summary>Regla 14: ausencias planificadas del analista.</summary>
public sealed class AusenciaService(RrhhDbContext db) : IAusenciaService
{
    public async Task<Ausencia> RegistrarAsync(
        int analistaId, DateTime inicio, DateTime fin, string? motivo, CancellationToken ct = default)
    {
        if (fin <= inicio)
            throw new InvalidOperationException("La fecha de fin debe ser posterior a la de inicio.");

        var ausencia = new Ausencia
        {
            AnalistaId = analistaId,
            FechaInicio = inicio,
            FechaFin = fin,
            Motivo = motivo
        };

        db.Ausencias.Add(ausencia);
        await db.SaveChangesAsync(ct);

        return ausencia;
    }

    /// <summary>
    /// El periodo es inclusivo en ambos extremos: quien carga vacaciones del 1 al 15 espera estar
    /// cubierto el dia 15 completo, no hasta su medianoche inicial.
    /// </summary>
    public Task<bool> EstaAusenteAsync(int analistaId, DateTime momento, CancellationToken ct = default) =>
        db.Ausencias.AnyAsync(
            a => a.AnalistaId == analistaId && a.FechaInicio <= momento && a.FechaFin >= momento, ct);
}
