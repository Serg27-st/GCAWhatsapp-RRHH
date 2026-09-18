using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas;

/// <summary>
/// Regla 6 y Regla 11 en la salida (FUN-09, A3): cuando el postulante esta en proceso con dos
/// clientes a la vez, todo lo que recibe llega por el mismo hilo de WhatsApp (V1).
/// <para>
/// Sin decir de que empresa se habla, «te esperamos el lunes a las 9» es una cita a la que la persona
/// no sabe a donde ir. El prefijo lo resuelve en el propio mensaje, que es el unico lugar donde el
/// postulante lo va a leer.
/// </para>
/// </summary>
public static class PrefijoMultiCuenta
{
    /// <summary>
    /// Antepone «[Cuenta · Vacante] » si la persona tiene procesos vivos en mas de una cuenta. Si esta
    /// en una sola, el prefijo seria ruido en cada mensaje: no hay ambiguedad que aclarar.
    /// </summary>
    public static string Aplicar(
        string texto, IReadOnlyList<PostulacionVigente> postulaciones, int? cuentaContextoId)
    {
        if (string.IsNullOrWhiteSpace(texto) || cuentaContextoId is not { } cuentaId)
            return texto;

        var vivas = postulaciones
            .Where(p => p.Estado is EstadoPostulacion.EnProceso or EstadoPostulacion.Reingreso)
            .ToList();

        if (vivas.Select(p => p.CuentaId).Distinct().Count() < 2)
            return texto;

        // La mas reciente de la cuenta en contexto: es sobre la que se esta conversando ahora.
        var proceso = vivas
            .Where(p => p.CuentaId == cuentaId)
            .OrderByDescending(p => p.FechaUltimaActividad)
            .FirstOrDefault();

        if (proceso is null)
            return texto;

        var prefijo = $"[{proceso.Cuenta} · {proceso.Vacante}] ";

        // Reenviar el mismo texto —un doble clic, o un reintento de la bandeja— no lo prefija dos veces.
        return texto.StartsWith(prefijo, StringComparison.Ordinal) ? texto : prefijo + texto;
    }
}
