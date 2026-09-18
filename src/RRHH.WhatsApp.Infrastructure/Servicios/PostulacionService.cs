using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Domain.Reglas;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Infrastructure.Servicios;

/// <summary>
/// La persona aplicando a una vacante concreta: lo que recorre el kanban (Regla 13) y lo que hace
/// posible la Regla 6, dos postulaciones independientes sobre un unico hilo de WhatsApp.
/// </summary>
public sealed class PostulacionService(RrhhDbContext db, TimeProvider reloj, ILogger<PostulacionService> log)
    : IPostulacionService
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
            existente.FechaUltimaActividad = reloj.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);

            return existente;
        }

        var titular = await db.AnalistaCuentas
            .AsNoTracking()
            .Where(ac => ac.CuentaId == hc.CuentaId && !ac.EsBackup)
            .Select(ac => (int?)ac.AnalistaId)
            .FirstOrDefaultAsync(ct);

        var ahora = reloj.GetUtcNow().UtcDateTime;

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

    public async Task<ResultadoMovimiento> MoverEtapaKanbanAsync(
        int postulacionId, int etapaId, int analistaId, CancellationToken ct = default)
    {
        var postulacion = await db.Postulaciones.FirstAsync(p => p.PostulacionId == postulacionId, ct);

        var etapa = await db.EtapasKanban.AsNoTracking().FirstOrDefaultAsync(e => e.EtapaId == etapaId, ct)
            ?? throw new InvalidOperationException($"No existe la etapa {etapaId}.");

        var etapaAnterior = postulacion.EtapaKanbanId;
        var estadoAnterior = postulacion.Estado;
        var ahora = reloj.GetUtcNow().UtcDateTime;

        postulacion.EtapaKanbanId = etapaId;
        postulacion.FechaCambioEtapa = ahora;
        postulacion.FechaUltimaActividad = ahora;

        // COR-11: la columna dice el desenlace (ARQ-06), no su nombre. Sacar la tarjeta de una columna
        // final revierte el cierre —reconsiderar un descarte es parte del trabajo del analista (AL7)—,
        // y un Reingreso se conserva porque no lo decide el tablero (A2).
        postulacion.Estado = etapa.EstadoResultante
            ?? (estadoAnterior is EstadoPostulacion.Contratado or EstadoPostulacion.Descartado
                ? EstadoPostulacion.EnProceso
                : estadoAnterior);

        db.Auditorias.Add(Auditar(postulacionId, analistaId, "MovimientoKanban",
            $"De la etapa {etapaAnterior} a {etapaId} ({etapa.Nombre})."));

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

            return new ResultadoMovimiento(estadoAnterior, estadoAnterior, Aplicado: false);
        }

        return new ResultadoMovimiento(estadoAnterior, postulacion.Estado, Aplicado: true);
    }

    public async Task<IReadOnlyList<int>> MarcarEstadoAsync(
        int postulanteId, int cuentaId, TipoEstadoPostulante tipo,
        string? motivo, int analistaId, CancellationToken ct = default)
    {
        // Regla 7: el motivo es obligatorio en blacklist y opcional en whitelist. Sin motivo, un
        // descarte no se puede explicar despues ni al postulante ni en una auditoria.
        if (tipo == TipoEstadoPostulante.Blacklist && string.IsNullOrWhiteSpace(motivo))
            throw new InvalidOperationException("El motivo es obligatorio para marcar blacklist.");

        // Un solo instante para toda la operacion: la marca, la auditoria y el descarte en cascada
        // de mas abajo son un mismo hecho de negocio.
        var ahora = reloj.GetUtcNow().UtcDateTime;

        db.EstadosPostulanteCuenta.Add(new EstadoPostulanteCuenta
        {
            PostulanteId = postulanteId,
            CuentaId = cuentaId,
            Tipo = tipo,
            Motivo = motivo,
            AnalistaId = analistaId,
            Fecha = ahora
        });

        db.Auditorias.Add(new Auditoria
        {
            EntidadTipo = nameof(Postulante),
            EntidadId = postulanteId.ToString(),
            AnalistaId = analistaId,
            Accion = tipo.ToString(),
            Detalle = motivo,
            Fecha = ahora
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
                postulacion.FechaUltimaActividad = ahora;

                if (etapaDescartado is { } etapaId)
                {
                    postulacion.EtapaKanbanId = etapaId;
                    postulacion.FechaCambioEtapa = ahora;
                }

                descartadas.Add(postulacion.PostulacionId);
            }
        }

        await db.SaveChangesAsync(ct);

        return descartadas;
    }

    /// <summary>
    /// La columna de descarte del tablero, por su desenlace y no por su nombre (COR-11): renombrar la
    /// columna en la administracion no puede cambiar lo que hace la Regla 7. Nula si no esta sembrada.
    /// </summary>
    private async Task<int?> EtapaFinalDescartadoAsync(CancellationToken ct) =>
        await db.EtapasKanban
            .AsNoTracking()
            .Where(e => e.EstadoResultante == EstadoPostulacion.Descartado)
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

    public Task<Postulacion?> ObtenerPorIdAsync(int postulacionId, CancellationToken ct = default) =>
        db.Postulaciones.AsNoTracking().FirstOrDefaultAsync(p => p.PostulacionId == postulacionId, ct);

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



    public async Task MarcarReingresoAsync(int postulacionId, int analistaId, CancellationToken ct = default)
    {
        var postulacion = await db.Postulaciones.FirstAsync(p => p.PostulacionId == postulacionId, ct);

        var ahora = reloj.GetUtcNow().UtcDateTime;

        postulacion.Estado = EstadoPostulacion.Reingreso;
        postulacion.FechaReingreso = ahora;
        postulacion.FechaUltimaActividad = ahora;

        // A11: si el descarte habia dejado pedido el cierre de cortesia, ya no corresponde: el
        // proceso sigue vivo y ese mensaje diria lo contrario.
        postulacion.CierreCortesiaPendiente = false;

        // Vuelve al tablero: la primera columna que no sea final. Dejarla en «Descartado» mostraria
        // una tarjeta viva en una columna de cierre.
        var primera = await db.EtapasKanban
            .AsNoTracking()
            .Where(e => !e.EsFinal)
            .OrderBy(e => e.Orden)
            .Select(e => (int?)e.EtapaId)
            .FirstOrDefaultAsync(ct);

        if (primera is { } etapaId)
        {
            postulacion.EtapaKanbanId = etapaId;
            postulacion.FechaCambioEtapa = ahora;
        }

        db.Auditorias.Add(Auditar(postulacionId, analistaId, "Reingreso",
            "La persona vuelve a un proceso vivo (A2)."));

        await db.SaveChangesAsync(ct);

        log.LogInformation(
            "Postulacion {PostulacionId} marcada como reingreso por el analista {AnalistaId}.",
            postulacionId, analistaId);
    }

    public async Task MarcarCierrePendienteAsync(
        int postulacionId, bool enviar, bool automatico, CancellationToken ct = default)
    {
        var postulacion = await db.Postulaciones.FirstAsync(p => p.PostulacionId == postulacionId, ct);

        // P4: el cierre sale una sola vez por postulacion. Si ya salio, descartarla de nuevo —o
        // reprocesar el evento— no puede volver a pedirlo.
        if (postulacion.FechaCierreCortesia is not null)
            return;

        // A11: sale por defecto, pero el analista puede decir que no en el dialogo de descarte, y
        // la operacion entera puede apagarlo con cierre.automatico.
        postulacion.CierreCortesiaPendiente = enviar && automatico;

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<int>> ListarCierresPendientesAsync(
        int maximo, CancellationToken ct = default) =>
        await db.Postulaciones
            .AsNoTracking()
            // FUN-10: lo pedido y todavia no enviado. Suele quedar pendiente porque se descarto fuera
            // del horario de atencion: el barrido lo toma cuando la jornada abre.
            .Where(p => p.CierreCortesiaPendiente && p.FechaCierreCortesia == null)
            .OrderBy(p => p.FechaUltimaActividad)
            .Take(maximo)
            .Select(p => p.PostulacionId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<int>> ListarPorArchivarAsync(
        int dias, int maximo, CancellationToken ct = default)
    {
        var limite = reloj.GetUtcNow().UtcDateTime.AddDays(-dias);

        return await db.Postulaciones
            .AsNoTracking()
            // FUN-11: en curso o descartadas, sin actividad desde el limite. Contratado y Reingreso no
            // se archivan por silencio (Regla 16, A2).
            .Where(p => (p.Estado == EstadoPostulacion.EnProceso || p.Estado == EstadoPostulacion.Descartado)
                     && p.FechaUltimaActividad <= limite)
            .OrderBy(p => p.FechaUltimaActividad)
            .Take(maximo)
            .Select(p => p.PostulacionId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<int>> ListarPorAvisarArchivadoAsync(
        int dias, int diasAviso, int maximo, CancellationToken ct = default)
    {
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var desdeAviso = ahora.AddDays(-(dias - diasAviso));

        return await db.Postulaciones
            .AsNoTracking()
            // A14: en curso, dentro de la ventana de aviso y sin el aviso todavia. Las ya vencidas las
            // toma ListarPorArchivarAsync.
            .Where(p => p.Estado == EstadoPostulacion.EnProceso
                     && p.FechaAvisoArchivado == null
                     && p.FechaUltimaActividad <= desdeAviso)
            .OrderBy(p => p.FechaUltimaActividad)
            .Take(maximo)
            .Select(p => p.PostulacionId)
            .ToListAsync(ct);
    }
    public async Task ArchivarAsync(int postulacionId, string motivo, CancellationToken ct = default)
    {
        var postulacion = await db.Postulaciones.FirstAsync(p => p.PostulacionId == postulacionId, ct);

        if (postulacion.Estado == EstadoPostulacion.Archivada)
            return;

        postulacion.Estado = EstadoPostulacion.Archivada;

        db.Auditorias.Add(Auditar(postulacionId, null, "Archivado", motivo));

        await db.SaveChangesAsync(ct);

        log.LogInformation("Postulacion {PostulacionId} archivada: {Motivo}", postulacionId, motivo);
    }

    public async Task SellarAsync(int postulacionId, MarcaPostulacion marca, CancellationToken ct = default)
    {
        var postulacion = await db.Postulaciones.FirstAsync(p => p.PostulacionId == postulacionId, ct);
        var ahora = reloj.GetUtcNow().UtcDateTime;

        switch (marca)
        {
            case MarcaPostulacion.AvisoArchivado:
                postulacion.FechaAvisoArchivado = ahora;
                break;

            case MarcaPostulacion.CierreCortesiaEnviado:
                // A11: se sella y se apaga el pendiente a la vez, para que el barrido no lo vuelva a tomar.
                postulacion.FechaCierreCortesia = ahora;
                postulacion.CierreCortesiaPendiente = false;
                break;

            case MarcaPostulacion.CierreCortesiaPendiente:
                postulacion.CierreCortesiaPendiente = true;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(marca), marca, "No hay sello para esa marca.");
        }

        await db.SaveChangesAsync(ct);
    }
    private Auditoria Auditar(int postulacionId, int? analistaId, string accion, string? detalle) =>
        new()
        {
            EntidadTipo = nameof(Postulacion),
            EntidadId = postulacionId.ToString(),
            AnalistaId = analistaId,
            Accion = accion,
            Detalle = detalle,
            Fecha = reloj.GetUtcNow().UtcDateTime
        };
}
