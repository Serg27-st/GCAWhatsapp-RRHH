using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Infrastructure.Servicios;

public sealed class CuentaService(RrhhDbContext db) : ICuentaService
{
    public async Task<IReadOnlyList<Cuenta>> ListarConVacantesAbiertasAsync(CancellationToken ct = default) =>
        await db.Cuentas
            .AsNoTracking()
            .Where(c => c.Activo && c.Vacantes.Any(v => v.Estado == EstadoHc.Abierta))
            .OrderBy(c => c.Nombre)
            .ToListAsync(ct);

    public Task<Hc?> ObtenerVacanteAsync(int hcId, CancellationToken ct = default) =>
        db.Hcs.AsNoTracking().FirstOrDefaultAsync(h => h.HcId == hcId, ct);

    public Task<Cuenta?> ObtenerPorIdAsync(int cuentaId, CancellationToken ct = default) =>
        db.Cuentas.AsNoTracking().FirstOrDefaultAsync(c => c.CuentaId == cuentaId, ct);

    public async Task<IReadOnlyList<Hc>> ListarVacantesAbiertasAsync(int cuentaId, CancellationToken ct = default) =>
        await db.Hcs
            .AsNoTracking()
            .Where(h => h.CuentaId == cuentaId && h.Estado == EstadoHc.Abierta)
            .OrderBy(h => h.Titulo)
            .ToListAsync(ct);

    public async Task<Hc> CrearVacanteAsync(
        int cuentaId, string titulo, string? urlJobForms, int analistaId, CancellationToken ct = default)
    {
        var existeCuenta = await db.Cuentas.AnyAsync(c => c.CuentaId == cuentaId && c.Activo, ct);

        if (!existeCuenta)
            throw new InvalidOperationException($"No existe una cuenta activa con id {cuentaId}.");

        var vacante = new Hc
        {
            CuentaId = cuentaId,
            Titulo = titulo,
            Estado = EstadoHc.Abierta,
            UrlJobForms = urlJobForms,
            FechaCreacion = DateTime.UtcNow
        };

        db.Hcs.Add(vacante);
        db.Auditorias.Add(Auditar(vacante.HcId, analistaId, "VacanteCreada", titulo));

        await db.SaveChangesAsync(ct);

        return vacante;
    }

    public async Task CerrarVacanteAsync(int hcId, int analistaId, CancellationToken ct = default)
    {
        var vacante = await db.Hcs.FirstOrDefaultAsync(h => h.HcId == hcId, ct)
            ?? throw new InvalidOperationException($"No existe la vacante {hcId}.");

        if (vacante.Estado == EstadoHc.Cerrada)
            return;

        vacante.Estado = EstadoHc.Cerrada;
        vacante.FechaCierre = DateTime.UtcNow;

        // Regla 20: desde aca el bot deja de ofrecerla y el formulario deja de aceptar envios.
        // El formulario de Google, en cambio, hay que desactivarlo a mano (ver docs/decisiones.md).
        db.Auditorias.Add(Auditar(hcId, analistaId, "VacanteCerrada", vacante.Titulo));

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<(Cuenta Cuenta, bool EsBackup, int VacantesAbiertas)>> ListarDeAnalistaAsync(
        int analistaId, CancellationToken ct = default)
    {
        var filas = await db.AnalistaCuentas
            .AsNoTracking()
            .Include(ac => ac.Cuenta)
            .Where(ac => ac.AnalistaId == analistaId && ac.Cuenta!.Activo)
            .Select(ac => new
            {
                ac.Cuenta,
                ac.EsBackup,
                Abiertas = ac.Cuenta!.Vacantes.Count(v => v.Estado == EstadoHc.Abierta)
            })
            .OrderBy(f => f.Cuenta!.Nombre)
            .ToListAsync(ct);

        return [.. filas.Select(f => (f.Cuenta!, f.EsBackup, f.Abiertas))];
    }


    public async Task<Cuenta> CrearAsync(string nombre, CancellationToken ct = default)
    {
        var limpio = nombre.Trim();

        if (await db.Cuentas.AnyAsync(c => c.Nombre == limpio, ct))
            throw new InvalidOperationException($"Ya existe una cuenta llamada '{limpio}'.");

        var cuenta = new Cuenta { Nombre = limpio, Activo = true };

        db.Cuentas.Add(cuenta);

        await db.SaveChangesAsync(ct);

        return cuenta;
    }

    public async Task<IReadOnlyList<Cuenta>> ListarTodasAsync(CancellationToken ct = default) =>
        await db.Cuentas.AsNoTracking().OrderBy(c => c.Nombre).ToListAsync(ct);

    public async Task<IReadOnlyList<(Cuenta Cuenta, Analista? Titular, Analista? Respaldo, int VacantesAbiertas)>>
        ListarConDotacionAsync(CancellationToken ct = default)
    {
        var filas = await db.Cuentas
            .AsNoTracking()
            .OrderBy(c => c.Nombre)
            .Select(c => new
            {
                Cuenta = c,
                Titular = c.Asignaciones.Where(a => !a.EsBackup).Select(a => a.Analista).FirstOrDefault(),
                Respaldo = c.Asignaciones.Where(a => a.EsBackup).Select(a => a.Analista).FirstOrDefault(),
                Abiertas = c.Vacantes.Count(v => v.Estado == EstadoHc.Abierta)
            })
            .ToListAsync(ct);

        return [.. filas.Select(f => (f.Cuenta, f.Titular, f.Respaldo, f.Abiertas))];
    }

    public async Task AsignarAnalistaAsync(
        int cuentaId, int analistaId, bool esBackup, CancellationToken ct = default)
    {
        if (!await db.Cuentas.AnyAsync(c => c.CuentaId == cuentaId, ct))
            throw new InvalidOperationException($"No existe la cuenta {cuentaId}.");

        if (!await db.Analistas.AnyAsync(a => a.AnalistaId == analistaId && a.Activo, ct))
            throw new InvalidOperationException($"No existe un analista activo con id {analistaId}.");

        var asignaciones = await db.AnalistaCuentas.Where(ac => ac.CuentaId == cuentaId).ToListAsync(ct);

        // Que el titular y el respaldo sean personas distintas lo garantiza el indice unico
        // (AnalistaId, CuentaId): un analista tiene una sola fila por cuenta, con un solo rol.
        var existente = asignaciones.FirstOrDefault(ac => ac.AnalistaId == analistaId);

        if (existente is not null)
        {
            existente.EsBackup = esBackup;
        }
        else
        {
            // Regla 1: la cuenta tiene un titular, y la 2 un respaldo. Se reemplaza en vez de
            // acumular: con dos titulares, el enrutamiento elegiria uno de forma arbitraria.
            var ocupante = asignaciones.FirstOrDefault(ac => ac.EsBackup == esBackup);

            if (ocupante is not null)
                db.AnalistaCuentas.Remove(ocupante);

            db.AnalistaCuentas.Add(new AnalistaCuenta
            {
                CuentaId = cuentaId,
                AnalistaId = analistaId,
                EsBackup = esBackup
            });
        }

        db.Auditorias.Add(new Auditoria
        {
            EntidadTipo = nameof(Cuenta),
            EntidadId = cuentaId.ToString(),
            Detalle = $"Analista {analistaId}.",
            Accion = esBackup ? "RespaldoAsignado" : "TitularAsignado",
            Fecha = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task QuitarAnalistaAsync(int cuentaId, int analistaId, CancellationToken ct = default)
    {
        var asignacion = await db.AnalistaCuentas
            .FirstOrDefaultAsync(ac => ac.CuentaId == cuentaId && ac.AnalistaId == analistaId, ct);

        if (asignacion is null)
            return;

        db.AnalistaCuentas.Remove(asignacion);

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<HcCampoOpcional>> ListarCamposOpcionalesAsync(
        int hcId, CancellationToken ct = default) =>
        await db.HcCamposOpcionales
            .AsNoTracking()
            .Where(c => c.HcId == hcId)
            .OrderBy(c => c.CampoId)
            .ToListAsync(ct);

    public async Task ReemplazarCamposOpcionalesAsync(
        int hcId, IReadOnlyList<HcCampoOpcional> campos, int analistaId, CancellationToken ct = default)
    {
        var existeVacante = await db.Hcs.AnyAsync(h => h.HcId == hcId, ct);

        if (!existeVacante)
            throw new InvalidOperationException($"No existe la vacante {hcId}.");

        foreach (var campo in campos.Where(c => string.IsNullOrWhiteSpace(c.NombreCampo)))
            throw new InvalidOperationException("Un campo opcional necesita nombre.");

        // Dos campos con el mismo nombre dejarian el formulario con dos preguntas identicas y sin
        // forma de distinguir las respuestas al leer el DatosJson.
        var repetido = campos
            .GroupBy(c => c.NombreCampo.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        if (repetido is not null)
            throw new InvalidOperationException($"El campo '{repetido.Key}' esta repetido.");

        var actuales = await db.HcCamposOpcionales.Where(c => c.HcId == hcId).ToListAsync(ct);

        db.HcCamposOpcionales.RemoveRange(actuales);

        foreach (var campo in campos)
        {
            db.HcCamposOpcionales.Add(new HcCampoOpcional
            {
                HcId = hcId,
                NombreCampo = campo.NombreCampo.Trim(),
                Tipo = campo.Tipo,
                Activo = campo.Activo
            });
        }

        db.Auditorias.Add(Auditar(hcId, analistaId, "CamposOpcionalesActualizados",
            $"{campos.Count} campo(s) configurados."));

        await db.SaveChangesAsync(ct);
    }
    private static Auditoria Auditar(int hcId, int analistaId, string accion, string? detalle) =>
        new()
        {
            EntidadTipo = nameof(Hc),
            EntidadId = hcId.ToString(),
            AnalistaId = analistaId,
            Accion = accion,
            Detalle = detalle,
            Fecha = DateTime.UtcNow
        };
}
