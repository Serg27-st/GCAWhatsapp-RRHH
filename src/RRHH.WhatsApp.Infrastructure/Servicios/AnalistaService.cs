using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Infrastructure.Servicios;

public sealed class AnalistaService(
    RrhhDbContext db,
    IConversacionService conversaciones,
    IAlertaOperativaService alertas,
    IUnidadTrabajo unidad,
    TimeProvider reloj) : IAnalistaService
{
    public async Task<IReadOnlyList<Analista>> ListarActivosAsync(CancellationToken ct = default) =>
        await db.Analistas
            .AsNoTracking()
            .Where(a => a.Activo)
            .OrderBy(a => a.Nombre)
            .ToListAsync(ct);



    public async Task<EstadoSeguridad?> ObtenerEstadoSeguridadAsync(
        int analistaId, CancellationToken ct = default) =>
        await db.Analistas
            .AsNoTracking()
            .Where(a => a.AnalistaId == analistaId)
            .Select(a => new EstadoSeguridad(a.Activo, a.VersionSeguridad))
            .FirstOrDefaultAsync(ct);

    public async Task<ResumenCartera> ObtenerCarteraAsync(int analistaId, CancellationToken ct = default)
    {
        var conversacionesACargo = await db.Conversaciones
            .CountAsync(c => c.AnalistaAtendiendoId == analistaId && c.Estado != EstadoConversacion.Archivada, ct);

        var asignaciones = await db.AnalistaCuentas
            .AsNoTracking()
            .Where(ac => ac.AnalistaId == analistaId)
            .ToListAsync(ct);

        return new ResumenCartera(
            conversacionesACargo,
            asignaciones.Count(ac => !ac.EsBackup),
            asignaciones.Count(ac => ac.EsBackup));
    }

    public async Task ActualizarAsync(
        int analistaId, string? nombre, RolAnalista? rol, bool? activo, int autorId,
        CancellationToken ct = default)
    {
        var analista = await db.Analistas.FirstOrDefaultAsync(a => a.AnalistaId == analistaId, ct)
            ?? throw new InvalidOperationException($"No existe el analista {analistaId}.");

        // Dejar de atender conversaciones es lo que obliga a mover su bandeja: da igual si fue por una
        // baja o por un cambio de rol, porque Jefatura y Sistemas no reciben conversaciones (V23).
        var atendia = analista.Activo && analista.Rol == RolAnalista.Analista;
        var seguiraAtendiendo = (activo ?? analista.Activo) && (rol ?? analista.Rol) == RolAnalista.Analista;
        var dejaDeAtender = atendia && !seguiraAtendiendo;

        await unidad.EjecutarAsync(async c =>
        {
            if (!string.IsNullOrWhiteSpace(nombre))
                analista.Nombre = nombre.Trim();

            if (rol is { } nuevoRol)
                analista.Rol = nuevoRol;

            if (activo is { } estado)
                analista.Activo = estado;

            if (dejaDeAtender)
            {
                // ARQ-11: lo que le quita el acceso o las conversaciones tiene que sacar tambien sus
                // sesiones abiertas; si no, sigue trabajando con el token que ya tenia.
                analista.VersionSeguridad++;

                await db.SaveChangesAsync(c);

                await conversaciones.ReasignarCarteraAsync(analistaId, autorId, c);
                await LiberarCuentasAsync(analista, c);
            }

            db.Auditorias.Add(new Auditoria
            {
                EntidadTipo = nameof(Analista),
                EntidadId = analistaId.ToString(),
                AnalistaId = autorId,
                Accion = "AnalistaActualizado",
                Detalle = $"Nombre: {nombre ?? "sin cambios"}. Rol: {rol?.ToString() ?? "sin cambios"}. " +
                          $"Activo: {activo?.ToString() ?? "sin cambios"}.",
                Fecha = reloj.GetUtcNow().UtcDateTime
            });

            await db.SaveChangesAsync(c);
        }, ct);
    }

    /// <summary>
    /// FUN-19: sus cuentas quedan sin titular o sin respaldo. La asignacion se quita —dejarla apuntando
    /// a quien ya no atiende haria que el escalamiento y las ausencias siguieran nombrandolo— y queda
    /// una alerta para que Jefatura decida quien la cubre (ARQ-09).
    /// </summary>
    private async Task LiberarCuentasAsync(Analista analista, CancellationToken ct)
    {
        var asignaciones = await db.AnalistaCuentas
            .Where(ac => ac.AnalistaId == analista.AnalistaId)
            .ToListAsync(ct);

        if (asignaciones.Count == 0)
            return;

        db.AnalistaCuentas.RemoveRange(asignaciones);

        await db.SaveChangesAsync(ct);

        foreach (var asignacion in asignaciones)
        {
            await alertas.RegistrarAsync(
                asignacion.EsBackup ? TiposAlerta.CuentaSinRespaldo : TiposAlerta.CuentaSinTitular,
                $"cuenta:{asignacion.CuentaId}",
                $"El analista {analista.AnalistaId} dejo de atender conversaciones.",
                ct);
        }
    }

    public async Task<bool> CerrarSesionesAsync(int analistaId, CancellationToken ct = default)
    {
        var analista = await db.Analistas.FirstOrDefaultAsync(a => a.AnalistaId == analistaId, ct);

        if (analista is null)
            return false;

        analista.VersionSeguridad++;

        await db.SaveChangesAsync(ct);

        return true;
    }

    public async Task<IReadOnlyList<Analista>> ListarActivosPorRolAsync(
        RolAnalista rol, CancellationToken ct = default) =>
        await db.Analistas
            .AsNoTracking()
            .Where(a => a.Activo && a.Rol == rol)
            .OrderBy(a => a.Nombre)
            .ToListAsync(ct);
    public async Task<Analista> CrearAsync(
        string nombre, string email, RolAnalista rol, CancellationToken ct = default)
    {
        var correo = email.Trim().ToLowerInvariant();

        // El email es la identidad del analista y sera la llave cuando exista login: dos filas con
        // el mismo correo dejarian el sistema sin saber a cual de las dos entra la persona.
        if (await db.Analistas.AnyAsync(a => a.Email == correo, ct))
            throw new InvalidOperationException($"Ya existe un analista con el email '{correo}'.");

        var analista = new Analista
        {
            Nombre = nombre.Trim(),
            Email = correo,
            Rol = rol,
            Activo = true
        };

        db.Analistas.Add(analista);

        await db.SaveChangesAsync(ct);

        return analista;
    }
    public Task<Analista?> ObtenerPorIdAsync(int analistaId, CancellationToken ct = default) =>
        db.Analistas.AsNoTracking().FirstOrDefaultAsync(a => a.AnalistaId == analistaId, ct);
}
