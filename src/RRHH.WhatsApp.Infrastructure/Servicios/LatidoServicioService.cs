using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Infrastructure.Servicios;

public sealed class LatidoServicioService(RrhhDbContext db, TimeProvider reloj) : ILatidoServicio
{
    public async Task RegistrarAsync(
        string servicio, TimeSpan tolerancia, string? detalle = null, CancellationToken ct = default)
    {
        var latido = await db.LatidosServicio.FirstOrDefaultAsync(l => l.Servicio == servicio, ct);

        if (latido is null)
        {
            latido = new LatidoServicio { Servicio = servicio };
            db.LatidosServicio.Add(latido);
        }

        latido.FechaUtc = reloj.GetUtcNow().UtcDateTime;
        latido.ToleranciaSegundos = (int)tolerancia.TotalSeconds;
        latido.Detalle = detalle;

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<LatidoServicio>> ListarAsync(CancellationToken ct = default) =>
        await db.LatidosServicio.AsNoTracking().OrderBy(l => l.Servicio).ToListAsync(ct);
}
