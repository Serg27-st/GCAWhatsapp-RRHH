namespace RRHH.WhatsApp.Infrastructure.Servicios;

/// <summary>
/// Conversion entre UTC y la hora de Lima. En base de datos todo es UTC; el horario de atencion,
/// los reportes y lo que ve el analista son hora local.
/// <para>
/// Peru no aplica horario de verano, asi que el desfase es fijo. Aun asi se busca la zona en el
/// sistema antes de caer al desfase fijo: si algun dia eso cambia, basta con que el servidor tenga
/// la zona actualizada.
/// </para>
/// </summary>
public static class ZonaHorariaPeru
{
    private static readonly TimeZoneInfo Zona = Resolver();

    private static TimeZoneInfo Resolver()
    {
        // Identificador IANA primero, que es el que funciona en Linux y en Windows con ICU.
        foreach (var id in new[] { "America/Lima", "SA Pacific Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // Se prueba el siguiente identificador.
            }
        }

        // Ultimo recurso: UTC-5 fijo, que es lo que rige en Peru desde 1994.
        return TimeZoneInfo.CreateCustomTimeZone("RRHH-Peru", TimeSpan.FromHours(-5), "Peru (UTC-5)", "Peru");
    }

    public static DateTime ALocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zona);

    public static DateTime AUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zona);
}
