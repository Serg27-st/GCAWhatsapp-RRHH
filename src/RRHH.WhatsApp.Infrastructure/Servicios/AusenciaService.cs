using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Infrastructure.Servicios;

/// <summary>Regla 14: ausencias planificadas del analista.</summary>
public sealed class AusenciaService(RrhhDbContext db, TimeProvider reloj) : IAusenciaService
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

    public async Task<IReadOnlyList<Ausencia>> ListarVigentesAsync(
        int analistaId, DateTime desdeUtc, CancellationToken ct = default) =>
        await db.Ausencias
            .AsNoTracking()
            .Where(a => a.AnalistaId == analistaId && a.FechaFin >= desdeUtc)
            .OrderBy(a => a.FechaInicio)
            .ToListAsync(ct);

    /// <summary>
    /// Se busca por analista y por id juntos: con el id solo, la ruta de un analista serviria para
    /// borrar la ausencia de otro con solo cambiar el numero.
    /// </summary>
    public async Task<bool> EliminarAsync(int analistaId, int ausenciaId, CancellationToken ct = default)
    {
        var ausencia = await db.Ausencias
            .FirstOrDefaultAsync(a => a.AusenciaId == ausenciaId && a.AnalistaId == analistaId, ct);

        if (ausencia is null)
            return false;

        db.Ausencias.Remove(ausencia);
        await db.SaveChangesAsync(ct);

        return true;
    }

    public async Task<IReadOnlyList<Ausencia>> ListarFinalizadasSinAvisoAsync(
        DateTime ahora, CancellationToken ct = default) =>
        await db.Ausencias
            .AsNoTracking()
            .Where(a => a.FechaFin < ahora && a.FechaAvisoRetorno == null)
            .OrderBy(a => a.FechaFin)
            .ToListAsync(ct);

    public async Task MarcarAvisoRetornoAsync(int ausenciaId, CancellationToken ct = default)
    {
        var ausencia = await db.Ausencias.FirstOrDefaultAsync(a => a.AusenciaId == ausenciaId, ct);

        // Borrada mientras tanto: no hay nada que sellar y nada que avisar.
        if (ausencia is null || ausencia.FechaAvisoRetorno is not null)
            return;

        ausencia.FechaAvisoRetorno = reloj.GetUtcNow().UtcDateTime;

        await db.SaveChangesAsync(ct);
    }
}
