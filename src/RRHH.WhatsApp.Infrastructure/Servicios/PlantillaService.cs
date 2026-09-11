using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Infrastructure.Servicios;

/// <summary>
/// Resuelve que plantilla aprobada usar y si la ventana de servicio sigue abierta.
/// Sin esta pieza, las Reglas 3, 9, 12 y 15 no tienen quien las ejecute.
/// </summary>
public sealed class PlantillaService(RrhhDbContext db, ILogger<PlantillaService> log) : IPlantillaService
{
    /// <summary>Duracion de la ventana de servicio de WhatsApp, medida desde el ultimo mensaje entrante.</summary>
    public static readonly TimeSpan VentanaServicio = TimeSpan.FromHours(24);

    public async Task<Plantilla?> ObtenerPlantillaParaEventoAsync(string clave, CancellationToken ct = default)
    {
        var plantilla = await db.Plantillas
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Clave == clave, ct);

        if (plantilla is null)
        {
            log.LogError("No existe la plantilla '{Clave}' en el catalogo.", clave);
            return null;
        }

        // Devolver una plantilla inactiva dejaria que el llamador la enviara igual. Como el
        // proyecto existe para dejar de recibir bloqueos, se corta aca y se avisa que falta el
        // tramite de aprobacion, que es una accion administrativa y no un problema de codigo.
        if (!plantilla.Activa)
        {
            log.LogWarning(
                "La plantilla '{Clave}' ({NombreMeta}) esta inactiva: falta su aprobacion en Meta. No se envia.",
                plantilla.Clave, plantilla.NombreMeta);

            return null;
        }

        return plantilla;
    }

    public async Task<bool> ValidarVentana24hAsync(int conversacionId, CancellationToken ct = default)
    {
        var ultimoEntrante = await db.Conversaciones
            .AsNoTracking()
            .Where(c => c.ConversacionId == conversacionId)
            .Select(c => c.FechaUltimoMensajeEntrante)
            .FirstOrDefaultAsync(ct);

        // Nunca escribio: no hay ventana abierta ni nada que responder.
        if (ultimoEntrante is not { } ultimo)
            return false;

        return DateTime.UtcNow - ultimo < VentanaServicio;
    }

    public async Task<IReadOnlyList<Plantilla>> ListarActivasAsync(CancellationToken ct = default) =>
        await db.Plantillas
            .AsNoTracking()
            .Where(p => p.Activa)
            .OrderBy(p => p.Clave)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Plantilla>> ListarTodasAsync(CancellationToken ct = default) =>
        await db.Plantillas
            .AsNoTracking()
            .OrderBy(p => p.Clave)
            .ToListAsync(ct);
}
