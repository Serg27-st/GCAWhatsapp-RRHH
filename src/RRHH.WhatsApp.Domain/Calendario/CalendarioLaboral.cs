using RRHH.WhatsApp.Domain.Entidades;

namespace RRHH.WhatsApp.Domain.Calendario;

/// <summary>
/// Aritmetica del horario laboral, sin E/S (V31, ARQ-08). Recibe los tramos ya cargados y un instante
/// en UTC; los tramos se leen en hora de Lima.
/// <para>
/// Vive en Domain para que la compartan la Regla 3 (proxima apertura), la Regla 2 (horas habiles del
/// escalamiento) y el panel de gerencia (primera respuesta en horas habiles, A15). Reporting no puede
/// referenciar Infrastructure, y dos implementaciones de "hora habil" terminarian midiendo el plazo de
/// la Regla 2 con otra vara que la del escalamiento que lo hace cumplir.
/// </para>
/// <para>
/// Los tramos no cruzan la medianoche: la administracion rechaza un tramo que termina antes de
/// empezar. Lo que si cruza la medianoche es el periodo fuera de horario, de un cierre a la apertura
/// siguiente.
/// </para>
/// </summary>
public static class CalendarioLaboral
{
    /// <summary>
    /// Tope del recorrido dia a dia al contar minutos. Coincide con el archivado de la Regla 16: una
    /// conversacion mas vieja que eso ya no esta activa, asi que no tiene sentido seguir contando.
    /// </summary>
    private const int DiasMaximosConteo = 90;

    /// <summary>Una semana y un dia: alcanza para encontrar un cierre o una apertura con cualquier horario semanal.</summary>
    private const int DiasBusqueda = 8;

    /// <summary>
    /// Sin tramos se asume dentro de horario: la Regla 3 solo manda un aviso informativo, y mandarlo
    /// de mas es ruido que sube los reportes de spam (Seccion 2.4).
    /// </summary>
    public static bool EstaEnHorario(IReadOnlyCollection<HorarioAtencion> tramos, DateTime momentoUtc)
    {
        if (tramos.Count == 0)
            return true;

        var local = ZonaHorariaPeru.ALocal(momentoUtc);
        var hora = TimeOnly.FromDateTime(local);

        return tramos.Any(t => t.DiaSemana == local.DayOfWeek && hora >= t.HoraInicio && hora < t.HoraFin);
    }

    /// <summary>
    /// Minutos de horario laboral entre dos instantes. Sin tramos se cuenta a reloj corrido: es
    /// preferible escalar de mas que dejar una conversacion sin atender porque nadie cargo la jornada.
    /// </summary>
    public static double MinutosHabilesEntre(IReadOnlyCollection<HorarioAtencion> tramos, DateTime desdeUtc, DateTime hastaUtc)
    {
        if (hastaUtc <= desdeUtc)
            return 0;

        if (tramos.Count == 0)
            return (hastaUtc - desdeUtc).TotalMinutes;

        var desde = ZonaHorariaPeru.ALocal(desdeUtc);
        var hasta = ZonaHorariaPeru.ALocal(hastaUtc);

        if (hasta - desde > TimeSpan.FromDays(DiasMaximosConteo))
            desde = hasta.AddDays(-DiasMaximosConteo);

        var porDia = tramos.ToLookup(t => t.DiaSemana);
        var total = 0d;

        for (var dia = desde.Date; dia <= hasta.Date; dia = dia.AddDays(1))
        {
            foreach (var tramo in porDia[dia.DayOfWeek])
            {
                // Interseccion entre el tramo laboral y el periodo que se esta midiendo.
                var inicio = Max(dia.Add(tramo.HoraInicio.ToTimeSpan()), desde);
                var fin = Min(dia.Add(tramo.HoraFin.ToTimeSpan()), hasta);

                if (fin > inicio)
                    total += (fin - inicio).TotalMinutes;
            }
        }

        return total;
    }

    /// <summary>
    /// Cuando empezo el periodo fuera de horario en el que cae <paramref name="momentoUtc"/>: el ultimo
    /// cierre anterior. Nulo si esta dentro de horario o si no hay tramos. Es la marca que permite un
    /// solo aviso fuera de horario por periodo (A8): dos mensajes del mismo periodo dan el mismo inicio.
    /// </summary>
    public static DateTime? InicioPeriodoFueraDeHorario(IReadOnlyCollection<HorarioAtencion> tramos, DateTime momentoUtc)
    {
        if (tramos.Count == 0 || EstaEnHorario(tramos, momentoUtc))
            return null;

        var local = ZonaHorariaPeru.ALocal(momentoUtc);

        var ultimoCierre = CierresYAperturas(tramos, local.Date.AddDays(-DiasBusqueda), local.Date)
            .Select(t => t.Cierre)
            .Where(cierre => cierre <= local)
            .DefaultIfEmpty()
            .Max();

        return ultimoCierre == default ? null : ZonaHorariaPeru.AUtc(ultimoCierre);
    }

    /// <summary>
    /// La proxima vez que empieza un tramo despues de <paramref name="momentoUtc"/>. Nula si no hay
    /// tramos. Es lo que el aviso fuera de horario le dice al postulante: cuando se retoma la atencion.
    /// </summary>
    public static DateTime? ProximaApertura(IReadOnlyCollection<HorarioAtencion> tramos, DateTime momentoUtc)
    {
        if (tramos.Count == 0)
            return null;

        var local = ZonaHorariaPeru.ALocal(momentoUtc);

        var proxima = CierresYAperturas(tramos, local.Date, local.Date.AddDays(DiasBusqueda))
            .Select(t => t.Apertura)
            .Where(apertura => apertura > local)
            .DefaultIfEmpty()
            .Min();

        return proxima == default ? null : ZonaHorariaPeru.AUtc(proxima);
    }

    /// <summary>
    /// El instante en que se cumplen <paramref name="minutos"/> minutos habiles contados desde
    /// <paramref name="desdeUtc"/>. Es lo que fija el vencimiento de una transferencia (FUN-07, A1):
    /// dos horas pedidas un viernes a las 17:00 vencen el lunes a las 10:00, no el sabado.
    /// <para>
    /// Sin tramos cargados no hay jornada que respetar y se suma a reloj corrido, igual que
    /// <see cref="MinutosHabilesEntre"/>, que en ese caso devuelve el tiempo completo.
    /// </para>
    /// </summary>
    public static DateTime SumarMinutosHabiles(
        IReadOnlyCollection<HorarioAtencion> tramos, DateTime desdeUtc, double minutos)
    {
        if (tramos.Count == 0 || minutos <= 0)
            return desdeUtc.AddMinutes(minutos);

        var local = ZonaHorariaPeru.ALocal(desdeUtc);
        var restantes = minutos;

        foreach (var (apertura, cierre) in CierresYAperturas(tramos, local.Date, local.Date.AddDays(DiasBusqueda)))
        {
            if (cierre <= local)
                continue;

            var inicio = apertura > local ? apertura : local;
            var disponibles = (cierre - inicio).TotalMinutes;

            if (disponibles >= restantes)
                return ZonaHorariaPeru.AUtc(inicio.AddMinutes(restantes));

            restantes -= disponibles;
        }

        // Mas alla del horizonte de busqueda: pasa solo con plazos larguisimos o con un horario de
        // pocas horas por semana. Se cae a reloj corrido antes que devolver una fecha inventada.
        return desdeUtc.AddMinutes(minutos);
    }
    /// <summary>
    /// Texto del horario para el postulante (es el parametro de la plantilla de la Regla 3). Agrupa dos
    /// veces: los tramos de cada dia, para que una jornada partida se lea "de 09:00 a 13:00 y de 14:00 a
    /// 18:00"; y los dias con la misma jornada, para no repetir siete lineas iguales.
    /// </summary>
    public static string Describir(IReadOnlyCollection<HorarioAtencion> tramos)
    {
        if (tramos.Count == 0)
            return "nuestro horario habitual de oficina";

        var jornadaPorDia = tramos
            .GroupBy(t => t.DiaSemana)
            .ToDictionary(
                g => g.Key,
                g => string.Join(" y ", g
                    .OrderBy(t => t.HoraInicio)
                    .Select(t => $"de {t.HoraInicio:HH\\:mm} a {t.HoraFin:HH\\:mm}")));

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

    /// <summary>Cada tramo de cada dia del rango, como instantes locales de apertura y cierre.</summary>
    private static IEnumerable<(DateTime Apertura, DateTime Cierre)> CierresYAperturas(
        IReadOnlyCollection<HorarioAtencion> tramos, DateTime desdeDia, DateTime hastaDia)
    {
        var porDia = tramos.ToLookup(t => t.DiaSemana);

        for (var dia = desdeDia; dia <= hastaDia; dia = dia.AddDays(1))
        {
            foreach (var tramo in porDia[dia.DayOfWeek])
                yield return (dia.Add(tramo.HoraInicio.ToTimeSpan()), dia.Add(tramo.HoraFin.ToTimeSpan()));
        }
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

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
