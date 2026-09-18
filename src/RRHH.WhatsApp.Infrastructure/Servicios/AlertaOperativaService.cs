using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Infrastructure.Servicios;

/// <summary>
/// Alertas operativas agrupadas (V32, ARQ-09). La agrupacion la garantiza el indice unico filtrado
/// sobre (Tipo, Clave) de las abiertas; esto solo suma ocurrencias o abre la fila.
/// </summary>
public sealed class AlertaOperativaService(RrhhDbContext db, TimeProvider reloj) : IAlertaOperativaService
{
    private const int LargoDetalle = 1000;

    public async Task RegistrarAsync(string tipo, string clave, string detalle, CancellationToken ct = default)
    {
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var recortado = detalle.Length <= LargoDetalle ? detalle : detalle[..LargoDetalle];

        if (await SumarAsync(tipo, clave, recortado, ahora, ct))
            return;

        var nueva = new AlertaOperativa
        {
            Tipo = tipo,
            Clave = clave,
            Detalle = recortado,
            Ocurrencias = 1,
            FechaPrimera = ahora,
            FechaUltima = ahora
        };

        db.AlertasOperativas.Add(nueva);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Otra ocurrencia abrio la misma alerta entre la consulta y el guardado: se suma a esa.
            db.Entry(nueva).State = EntityState.Detached;
            await SumarAsync(tipo, clave, recortado, ahora, ct);
        }
    }

    public async Task<IReadOnlyList<AlertaOperativa>> ListarAbiertasAsync(CancellationToken ct = default) =>
        await db.AlertasOperativas
            .AsNoTracking()
            .Where(a => a.FechaResuelta == null)
            .OrderByDescending(a => a.FechaUltima)
            .ToListAsync(ct);

    public async Task<bool> ResolverAsync(int alertaId, int analistaId, CancellationToken ct = default)
    {
        var alerta = await db.AlertasOperativas
            .FirstOrDefaultAsync(a => a.AlertaId == alertaId && a.FechaResuelta == null, ct);

        if (alerta is null)
            return false;

        alerta.FechaResuelta = reloj.GetUtcNow().UtcDateTime;
        alerta.ResueltaPorAnalistaId = analistaId;

        await db.SaveChangesAsync(ct);

        return true;
    }

    private async Task<bool> SumarAsync(string tipo, string clave, string detalle, DateTime ahora, CancellationToken ct)
    {
        var abierta = await db.AlertasOperativas
            .FirstOrDefaultAsync(a => a.Tipo == tipo && a.Clave == clave && a.FechaResuelta == null, ct);

        if (abierta is null)
            return false;

        abierta.Ocurrencias++;
        abierta.Detalle = detalle;
        abierta.FechaUltima = ahora;

        await db.SaveChangesAsync(ct);

        return true;
    }
}
