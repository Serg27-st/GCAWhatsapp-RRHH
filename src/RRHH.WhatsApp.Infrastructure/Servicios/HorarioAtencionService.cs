using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Calendario;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Infrastructure.Servicios;

/// <summary>
/// Regla 3 y apoyo a la Regla 2. Resuelve el horario laboral configurado, que puede ser propio de
/// una cuenta o general para toda la operacion.
/// <para>
/// Es una fachada (V31): carga los tramos y delega la aritmetica en <see cref="CalendarioLaboral"/>,
/// que es la misma que usa el panel de gerencia. Aca solo queda lo que necesita la base: elegir los
/// tramos y administrarlos.
/// </para>
/// </summary>
public sealed class HorarioAtencionService(RrhhDbContext db, TimeProvider reloj, ILogger<HorarioAtencionService> log)
    : IHorarioAtencionService
{
    public async Task<bool> EstaEnHorarioAsync(int? cuentaId, DateTime momentoUtc, CancellationToken ct = default)
    {
        var horarios = await ObtenerHorariosAsync(cuentaId, ct);

        if (horarios.Count == 0)
            log.LogWarning("No hay horario de atencion configurado. Se asume dentro de jornada.");

        return CalendarioLaboral.EstaEnHorario(horarios, momentoUtc);
    }

    public async Task<DateTime?> InicioPeriodoFueraDeHorarioAsync(
        int? cuentaId, DateTime momentoUtc, CancellationToken ct = default) =>
        CalendarioLaboral.InicioPeriodoFueraDeHorario(await ObtenerHorariosAsync(cuentaId, ct), momentoUtc);

    public async Task<DateTime?> ProximaAperturaAsync(
        int? cuentaId, DateTime momentoUtc, CancellationToken ct = default) =>
        CalendarioLaboral.ProximaApertura(await ObtenerHorariosAsync(cuentaId, ct), momentoUtc);

    public async Task<DateTime> SumarMinutosHabilesAsync(
        int? cuentaId, DateTime desdeUtc, double minutos, CancellationToken ct = default) =>
        CalendarioLaboral.SumarMinutosHabiles(await ObtenerHorariosAsync(cuentaId, ct), desdeUtc, minutos);

    public async Task<IReadOnlyList<HorarioAtencion>> ObtenerTramosAsync(
        int? cuentaId, CancellationToken ct = default) =>
        await db.HorariosAtencion
            .AsNoTracking()
            .Where(h => h.CuentaId == cuentaId)
            .OrderBy(h => h.DiaSemana)
            .ThenBy(h => h.HoraInicio)
            .ToListAsync(ct);

    public async Task ReemplazarTramosAsync(
        int? cuentaId, IReadOnlyList<HorarioAtencion> tramos, int analistaId, CancellationToken ct = default)
    {
        foreach (var tramo in tramos.Where(t => t.HoraFin <= t.HoraInicio))
        {
            throw new InvalidOperationException(
                $"El tramo del {tramo.DiaSemana} termina antes de empezar ({tramo.HoraInicio}-{tramo.HoraFin}).");
        }

        // Dos tramos superpuestos el mismo dia no son solo un dato feo: el calendario suma cada tramo
        // por separado, asi que la franja compartida se contaria dos veces y el escalamiento de la
        // Regla 2 saltaria antes de tiempo.
        foreach (var dia in tramos.GroupBy(t => t.DiaSemana))
        {
            var ordenados = dia.OrderBy(t => t.HoraInicio).ToList();

            for (var i = 1; i < ordenados.Count; i++)
            {
                if (ordenados[i].HoraInicio < ordenados[i - 1].HoraFin)
                {
                    throw new InvalidOperationException(
                        $"Los tramos del {dia.Key} se superponen: " +
                        $"{ordenados[i - 1].HoraInicio}-{ordenados[i - 1].HoraFin} y " +
                        $"{ordenados[i].HoraInicio}-{ordenados[i].HoraFin}.");
                }
            }
        }

        var actuales = await db.HorariosAtencion.Where(h => h.CuentaId == cuentaId).ToListAsync(ct);

        db.HorariosAtencion.RemoveRange(actuales);

        foreach (var tramo in tramos)
        {
            db.HorariosAtencion.Add(new HorarioAtencion
            {
                CuentaId = cuentaId,
                DiaSemana = tramo.DiaSemana,
                HoraInicio = tramo.HoraInicio,
                HoraFin = tramo.HoraFin
            });
        }

        db.Auditorias.Add(new Auditoria
        {
            EntidadTipo = nameof(HorarioAtencion),
            EntidadId = cuentaId?.ToString() ?? "general",
            AnalistaId = analistaId,
            Accion = "HorarioActualizado",
            Detalle = $"{tramos.Count} tramo(s).",
            Fecha = reloj.GetUtcNow().UtcDateTime
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<string> DescribirHorarioAsync(int? cuentaId, CancellationToken ct = default) =>
        CalendarioLaboral.Describir(await ObtenerHorariosAsync(cuentaId, ct));

    public async Task<double> MinutosHabilesEntreAsync(
        int? cuentaId, DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct = default) =>
        hastaUtc <= desdeUtc
            ? 0
            : CalendarioLaboral.MinutosHabilesEntre(await ObtenerHorariosAsync(cuentaId, ct), desdeUtc, hastaUtc);

    /// <summary>
    /// El horario propio de la cuenta gana sobre el general. Si la cuenta no tiene uno cargado,
    /// se usa el general (CuentaId nulo).
    /// </summary>
    private async Task<List<HorarioAtencion>> ObtenerHorariosAsync(int? cuentaId, CancellationToken ct)
    {
        if (cuentaId is { } id)
        {
            var propios = await db.HorariosAtencion
                .AsNoTracking()
                .Where(h => h.CuentaId == id)
                .ToListAsync(ct);

            if (propios.Count > 0)
                return propios;
        }

        return await db.HorariosAtencion
            .AsNoTracking()
            .Where(h => h.CuentaId == null)
            .ToListAsync(ct);
    }
}
