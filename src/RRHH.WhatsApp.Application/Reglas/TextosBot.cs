using System.Globalization;
using RRHH.WhatsApp.Domain.Calendario;

namespace RRHH.WhatsApp.Application.Reglas;

/// <summary>
/// Lo que dice el bot cuando puede hablar en texto libre (COR-03, P1).
/// <para>
/// Cada texto va en paralelo al borrador de su plantilla en <c>DatosSemilla</c>: el postulante tiene
/// que leer lo mismo dentro de la ventana de 24h que fuera de ella, donde el mensaje sale como
/// plantilla aprobada. Si se cambia uno, se cambia el otro.
/// </para>
/// </summary>
public static class TextosBot
{
    /// <summary>Hora de Lima escrita como la leeria una persona: «lunes 21 de setiembre a las 09:00».</summary>
    private static readonly CultureInfo Peru = CultureInfo.GetCultureInfo("es-PE");

    public static string ConfirmacionFormulario(string? nombre, string vacante) =>
        $"Gracias{Nombre(nombre)}. Recibimos tu ficha para la vacante {vacante}. " +
        "Un analista revisara tu postulacion y te escribira por este mismo chat.";

    public static string RecordatorioFormulario(string? nombre, string vacante, string enlace) =>
        $"Hola{Nombre(nombre)}. Notamos que aun no completas tu ficha de postulacion para la vacante {vacante}. " +
        $"Puedes hacerlo aqui: {enlace}";

    public static string CierreCortesia(string? nombre, string vacante) =>
        $"Hola{Nombre(nombre)}. Agradecemos tu interes en la vacante {vacante}. " +
        "En esta oportunidad continuaremos con otros perfiles, pero mantendremos tus datos para futuras convocatorias.";

    public static string VacanteCerrada(string vacante) =>
        $"La vacante {vacante} ya fue cubierta. " +
        "Puedes revisar nuestras otras convocatorias activas respondiendo a este mensaje.";

    /// <summary>
    /// FUN-04: el aviso dice cuando se retoma la atencion, no solo que esta cerrado. Sin
    /// <paramref name="proximaAperturaUtc"/> —no hay horario cargado— se omite esa parte en vez de
    /// inventar una fecha.
    /// </summary>
    public static string FueraDeHorario(string horario, DateTime? proximaAperturaUtc)
    {
        var aviso = $"Hola, recibimos tu mensaje. Nuestro horario de atencion es {horario}.";

        return proximaAperturaUtc is { } apertura
            ? $"{aviso} Te responderemos desde el {EnHoraDeLima(apertura)}."
            : $"{aviso} Un analista te respondera apenas retomemos la jornada.";
    }

    /// <summary>
    /// El nombre llega con el formulario (V2): antes de eso el hilo solo tiene un telefono. Sin nombre
    /// el saludo va seco —«Hola.»— en vez de dirigirse a un marcador de posicion.
    /// </summary>
    private static string Nombre(string? nombre) =>
        string.IsNullOrWhiteSpace(nombre) ? string.Empty : $" {nombre.Trim()}";

    private static string EnHoraDeLima(DateTime momentoUtc) =>
        ZonaHorariaPeru.ALocal(momentoUtc).ToString("dddd d 'de' MMMM 'a las' HH:mm", Peru);
}
