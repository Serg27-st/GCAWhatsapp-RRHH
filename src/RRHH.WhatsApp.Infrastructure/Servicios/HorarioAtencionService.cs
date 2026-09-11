using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Infrastructure.Servicios;

/// <summary>
/// Regla 3 y apoyo a la Regla 2. Resuelve el horario laboral configurado, que puede ser propio de
/// una cuenta o general para toda la operacion.
/// </summary>
public sealed class HorarioAtencionService(RrhhDbContext db, ILogger<HorarioAtencionService> log)
    : IHorarioAtencionService
{
    /// <summary>
    /// Tope del recorrido dia a dia. Coincide con el archivado de la Regla 16: una conversacion
    /// mas vieja que eso ya no esta activa, asi que no tiene sentido seguir contando.
    /// </summary>
    private const int DiasMaximos = 90;

    public async Task<bool> EstaEnHorarioAsync(int? cuentaId, DateTime momentoUtc, CancellationToken ct = default)
    {
        var horarios = await ObtenerHorariosAsync(cuentaId, ct);

        // Sin horario configurado no se puede afirmar que algo esta fuera de jornada. Se asume
        // dentro: la Regla 3 solo manda un aviso informativo, y mandarlo de mas es ruido que
        // sube los reportes de spam descritos en la Seccion 2.4.
        if (horarios.Count == 0)
        {
            log.LogWarning("No hay horario de atencion configurado. Se asume dentro de jornada.");
            return true;
        }

        var local = ZonaHorariaPeru.ALocal(momentoUtc);
        var hora = TimeOnly.FromDateTime(local);

        return horarios.Any(h => h.DiaSemana == local.DayOfWeek && hora >= h.HoraInicio && hora < h.HoraFin);
    }


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

        // Dos tramos superpuestos el mismo dia no son solo un dato feo: MinutosHabilesEntreAsync
        // suma cada tramo por separado, asi que la franja compartida se contaria dos veces y el
        // escalamiento de la Regla 2 saltaria antes de tiempo.
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
            Fecha = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
    }
    public async Task<string> DescribirHorarioAsync(int? cuentaId, CancellationToken ct = default)
    {
        var horarios = await ObtenerHorariosAsync(cuentaId, ct);

        if (horarios.Count == 0)
            return "nuestro horario habitual de oficina";

        // Este texto es el parametro de la plantilla de la Regla 3, o sea que lo lee el postulante.
        // Se agrupa dos veces: primero los tramos de cada dia, para que una jornada partida se lea
        // "de 09:00 a 13:00 y de 14:00 a 18:00"; despues los dias que comparten esa misma jornada,
        // para no repetir siete lineas iguales.
        var jornadaPorDia = horarios
            .GroupBy(h => h.DiaSemana)
            .ToDictionary(
                g => g.Key,
                g => string.Join(" y ", g
                    .OrderBy(h => h.HoraInicio)
                    .Select(h => $"de {h.HoraInicio:HH\\:mm} a {h.HoraFin:HH\\:mm}")));

        var bloques = jornadaPorDia
            .GroupBy(par => par.Value)
            .Select(g => new
            {
                Dias = g.Select(par => par.Key).OrderBy(OrdenDia).ToList(),
                Jornada = g.Key
            })
            .OrderBy(b => OrdenDia(b.Dias[0]))
            .Select(b => $"{DescribirDias(b.Dias)} {b.Jornada}");

        return string.Join(", ", bloques);
    }

    public async Task<double> MinutosHabilesEntreAsync(
        int? cuentaId, DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct = default)
    {
        if (hastaUtc <= desdeUtc)
            return 0;

        var horarios = await ObtenerHorariosAsync(cuentaId, ct);

        // Sin horario configurado se cuenta a reloj corrido: es preferible escalar de mas que
        // dejar una conversacion sin atender porque nadie cargo la jornada.
        if (horarios.Count == 0)
            return (hastaUtc - desdeUtc).TotalMinutes;

        var desde = ZonaHorariaPeru.ALocal(desdeUtc);
        var hasta = ZonaHorariaPeru.ALocal(hastaUtc);

        if (hasta - desde > TimeSpan.FromDays(DiasMaximos))
            desde = hasta.AddDays(-DiasMaximos);

        var porDia = horarios.ToLookup(h => h.DiaSemana);
        var total = 0d;

        for (var dia = desde.Date; dia <= hasta.Date; dia = dia.AddDays(1))
        {
            foreach (var tramo in porDia[dia.DayOfWeek])
            {
                var inicioTramo = dia.Add(tramo.HoraInicio.ToTimeSpan());
                var finTramo = dia.Add(tramo.HoraFin.ToTimeSpan());

                // Interseccion entre el tramo laboral y el periodo que se esta midiendo.
                var inicio = inicioTramo > desde ? inicioTramo : desde;
                var fin = finTramo < hasta ? finTramo : hasta;

                if (fin > inicio)
                    total += (fin - inicio).TotalMinutes;
            }
        }

        return total;
    }

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

    /// <summary>La semana laboral peruana empieza el lunes, no el domingo como en DayOfWeek.</summary>
    private static int OrdenDia(DayOfWeek dia) => ((int)dia + 6) % 7;

    private static string DescribirDias(List<DayOfWeek> dias)
    {
        var nombres = dias.Select(NombreDia).ToList();

        if (nombres.Count == 1)
            return nombres[0];

        // Si son consecutivos se expresan como rango: "lunes a viernes".
        var consecutivos = dias.Zip(dias.Skip(1), (a, b) => OrdenDia(b) - OrdenDia(a)).All(d => d == 1);

        return consecutivos
            ? $"{nombres[0]} a {nombres[^1]}"
            : string.Join(", ", nombres);
    }

    private static string NombreDia(DayOfWeek dia) => dia switch
    {
        DayOfWeek.Monday => "lunes",
        DayOfWeek.Tuesday => "martes",
        DayOfWeek.Wednesday => "miercoles",
        DayOfWeek.Thursday => "jueves",
        DayOfWeek.Friday => "viernes",
        DayOfWeek.Saturday => "sabado",
        _ => "domingo"
    };
}
