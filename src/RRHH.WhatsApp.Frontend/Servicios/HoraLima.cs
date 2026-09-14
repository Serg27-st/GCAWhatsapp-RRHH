namespace RRHH.WhatsApp.Frontend.Servicios;

/// <summary>
/// Las fechas llegan de la Api en UTC y se muestran en hora de Lima (UTC-5, sin horario de verano).
/// Un solo lugar para eso: con una copia por componente, la primera que alguien corrija deja a las
/// demás mostrando otra hora.
/// </summary>
public static class HoraLima
{
    /// <summary>Lima está en UTC-5 todo el año: no tiene horario de verano.</summary>
    private static readonly TimeSpan Desfase = TimeSpan.FromHours(-5);

    public static DateTime Desde(DateTime utc) => utc + Desfase;

    /// <summary>Hoy, en el calendario de Lima.</summary>
    public static DateOnly Hoy => DateOnly.FromDateTime(Desde(DateTime.UtcNow));

    /// <summary>Medianoche de Lima de ese día, expresada en UTC.</summary>
    public static DateTime InicioDelDia(DateOnly dia) =>
        dia.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) - Desfase;

    /// <summary>
    /// Último instante de ese día en Lima, en UTC. Las ausencias cuentan los dos extremos (Regla 14):
    /// quien carga del 1 al 15 espera estar cubierto el 15 entero.
    /// </summary>
    public static DateTime FinDelDia(DateOnly dia) => InicioDelDia(dia.AddDays(1)).AddTicks(-1);

    /// <summary>Cuánto hace, en la forma corta de la lista; pasado un mes, la fecha.</summary>
    public static string Hace(DateTime utc)
    {
        var transcurrido = DateTime.UtcNow - utc;

        return transcurrido switch
        {
            { TotalMinutes: < 1 } => "recién",
            { TotalHours: < 1 } => $"hace {(int)transcurrido.TotalMinutes} min",
            { TotalDays: < 1 } => $"hace {(int)transcurrido.TotalHours} h",
            { TotalDays: < 30 } => $"hace {(int)transcurrido.TotalDays} d",
            _ => Desde(utc).ToString("dd/MM/yyyy")
        };
    }
}
