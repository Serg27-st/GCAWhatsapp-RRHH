using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Infrastructure.Servicios;

/// <summary>
/// La persona aplicando a una vacante concreta: lo que recorre el kanban (Regla 13) y lo que hace
/// posible la Regla 6, dos postulaciones independientes sobre un unico hilo de WhatsApp.
/// </summary>
public sealed class PostulacionService(RrhhDbContext db, ILogger<PostulacionService> log) : IPostulacionService
{
    public async Task<Postulacion> CrearAsync(int postulanteId, int hcId, CancellationToken ct = default)
    {
        var hc = await db.Hcs.AsNoTracking().FirstOrDefaultAsync(h => h.HcId == hcId, ct)
            ?? throw new InvalidOperationException($"No existe la vacante {hcId}.");

        // Postular dos veces a la misma vacante es la misma postulacion: el postulante puede
        // reenviar el formulario y eso no debe abrirle una segunda tarjeta en el kanban.
        var existente = await db.Postulaciones
            .FirstOrDefaultAsync(p => p.PostulanteId == postulanteId && p.HcId == hcId, ct);

        if (existente is not null)
        {
            existente.FechaUltimaActividad = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            return existente;
        }

        var titular = await db.AnalistaCuentas
            .AsNoTracking()
            .Where(ac => ac.CuentaId == hc.CuentaId && !ac.EsBackup)
            .Select(ac => (int?)ac.AnalistaId)
            .FirstOrDefaultAsync(ct);

        var ahora = DateTime.UtcNow;

        var postulacion = new Postulacion
        {
            PostulanteId = postulanteId,
            HcId = hcId,
            // Denormalizado desde la vacante para filtrar la bandeja por cuenta sin un join extra.
            CuentaId = hc.CuentaId,
            AnalistaAsignadoId = titular,
            EtapaKanbanId = await PrimeraEtapaAsync(ct),
            Estado = EstadoPostulacion.EnProceso,
            FechaCreacion = ahora,
            FechaUltimaActividad = ahora
        };

        db.Postulaciones.Add(postulacion);

        await db.SaveChangesAsync(ct);

        return postulacion;
    }

    public async Task MoverEtapaKanbanAsync(
        int postulacionId, int etapaId, int analistaId, CancellationToken ct = default)
    {
        var postulacion = await db.Postulaciones.FirstAsync(p => p.PostulacionId == postulacionId, ct);

        var etapa = await db.EtapasKanban.AsNoTracking().FirstOrDefaultAsync(e => e.EtapaId == etapaId, ct)
            ?? throw new InvalidOperationException($"No existe la etapa {etapaId}.");

        var anterior = postulacion.EtapaKanbanId;
        var ahora = DateTime.UtcNow;

        postulacion.EtapaKanbanId = etapaId;
        postulacion.FechaCambioEtapa = ahora;
        postulacion.FechaUltimaActividad = ahora;

        // Las columnas finales del tablero son el desenlace del proceso: mover la tarjeta a
        // Contratado o Descartado es lo que cierra la postulacion, sin un segundo paso aparte.
        if (etapa.EsFinal)
        {
            postulacion.Estado = etapa.Nombre.StartsWith("Contratado", StringComparison.OrdinalIgnoreCase)
                ? EstadoPostulacion.Contratado
                : EstadoPostulacion.Descartado;
        }

        db.Auditorias.Add(Auditar(postulacionId, analistaId, "MovimientoKanban",
            $"De la etapa {anterior} a {etapaId} ({etapa.Nombre})."));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Dos analistas arrastrando la misma tarjeta a la vez. Gana el primero: reintentar a
            // ciegas dejaria la tarjeta en la columna del que llego tarde.
            log.LogInformation(
                "El movimiento de la postulacion {PostulacionId} se descarto: la fila cambio mientras tanto.",
                postulacionId);

            db.ChangeTracker.Clear();
        }
    }

    public async Task<IReadOnlyList<int>> MarcarEstadoAsync(
        int postulanteId, int cuentaId, TipoEstadoPostulante tipo,
        string? motivo, int analistaId, CancellationToken ct = default)
    {
        // Regla 7: el motivo es obligatorio en blacklist y opcional en whitelist. Sin motivo, un
        // descarte no se puede explicar despues ni al postulante ni en una auditoria.
        if (tipo == TipoEstadoPostulante.Blacklist && string.IsNullOrWhiteSpace(motivo))
            throw new InvalidOperationException("El motivo es obligatorio para marcar blacklist.");

        db.EstadosPostulanteCuenta.Add(new EstadoPostulanteCuenta
        {
            PostulanteId = postulanteId,
            CuentaId = cuentaId,
            Tipo = tipo,
            Motivo = motivo,
            AnalistaId = analistaId,
            Fecha = DateTime.UtcNow
        });

        db.Auditorias.Add(new Auditoria
        {
            EntidadTipo = nameof(Postulante),
            EntidadId = postulanteId.ToString(),
            AnalistaId = analistaId,
            Accion = tipo.ToString(),
            Detalle = motivo,
            Fecha = DateTime.UtcNow
        });

        var descartadas = new List<int>();

        // Marcar blacklist en una cuenta es descartar al postulante para esa cuenta: dejar sus
        // postulaciones EnProceso contradiria la marca y las seguiria mostrando en el kanban.
        // Es tambien lo que dispara el cierre de cortesia de la Regla 12.
        if (tipo == TipoEstadoPostulante.Blacklist)
        {
            var etapaDescartado = await EtapaFinalDescartadoAsync(ct);

            var vigentes = await db.Postulaciones
                .Where(p => p.PostulanteId == postulanteId
                         && p.CuentaId == cuentaId
                         && p.Estado == EstadoPostulacion.EnProceso)
                .ToListAsync(ct);

            foreach (var postulacion in vigentes)
            {
                postulacion.Estado = EstadoPostulacion.Descartado;
                postulacion.FechaUltimaActividad = DateTime.UtcNow;

                if (etapaDescartado is { } etapaId)
                {
                    postulacion.EtapaKanbanId = etapaId;
                    postulacion.FechaCambioEtapa = DateTime.UtcNow;
                }

                descartadas.Add(postulacion.PostulacionId);
            }
        }

        await db.SaveChangesAsync(ct);

        return descartadas;
    }

    /// <summary>La columna final de descarte del tablero. Nula si el catalogo no la tiene sembrada.</summary>
    private async Task<int?> EtapaFinalDescartadoAsync(CancellationToken ct) =>
        await db.EtapasKanban
            .AsNoTracking()
            .Where(e => e.EsFinal && e.Nombre.StartsWith("Descartado"))
            .Select(e => (int?)e.EtapaId)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Postulacion>> ObtenerTableroPorPostulanteAsync(
        int postulanteId, CancellationToken ct = default) =>
        await db.Postulaciones
            .AsNoTracking()
            .Include(p => p.Postulante)
            .Include(p => p.Hc)
            .Include(p => p.EtapaKanban)
            .Where(p => p.PostulanteId == postulanteId)
            .OrderByDescending(p => p.FechaUltimaActividad)
            .ToListAsync(ct);

    public async Task<EstadoPostulacion?> ObtenerEstadoAsync(int postulacionId, CancellationToken ct = default) =>
        await db.Postulaciones
            .AsNoTracking()
            .Where(p => p.PostulacionId == postulacionId)
            .Select(p => (EstadoPostulacion?)p.Estado)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Postulacion>> ObtenerTableroAsync(int hcId, CancellationToken ct = default) =>
        await db.Postulaciones
            .AsNoTracking()
            .Include(p => p.Postulante)
            .Include(p => p.EtapaKanban)
            .Where(p => p.HcId == hcId)
            .OrderBy(p => p.EtapaKanban!.Orden)
            .ThenByDescending(p => p.FechaUltimaActividad)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<EtapaKanban>> ListarEtapasAsync(CancellationToken ct = default) =>
        await db.EtapasKanban
            .AsNoTracking()
            .OrderBy(e => e.Orden)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Cuenta>> ObtenerOtrasCuentasEnProcesoAsync(
        int postulanteId, int cuentaExcluidaId, CancellationToken ct = default) =>
        await db.Postulaciones
            .AsNoTracking()
            .Where(p => p.PostulanteId == postulanteId
                     && p.Estado == EstadoPostulacion.EnProceso
                     && p.CuentaId != cuentaExcluidaId)
            .Select(p => p.Cuenta!)
            .Distinct()
            .ToListAsync(ct);

    /// <summary>Toda postulacion nace en la primera columna del tablero (Regla 13).</summary>
    private async Task<int> PrimeraEtapaAsync(CancellationToken ct) =>
        await db.EtapasKanban
            .AsNoTracking()
            .OrderBy(e => e.Orden)
            .Select(e => e.EtapaId)
            .FirstAsync(ct);

    private static Auditoria Auditar(int postulacionId, int? analistaId, string accion, string? detalle) =>
        new()
        {
            EntidadTipo = nameof(Postulacion),
            EntidadId = postulacionId.ToString(),
            AnalistaId = analistaId,
            Accion = accion,
            Detalle = detalle,
            Fecha = DateTime.UtcNow
        };
}
