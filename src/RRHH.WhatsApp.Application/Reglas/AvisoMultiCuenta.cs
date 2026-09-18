using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas;

/// <summary>
/// Regla 6 — el aviso de que el mismo DNI esta en proceso con otras cuentas.
/// <para>
/// Vive fuera de las reglas que lo emiten (R01 y R14) porque las dos tienen que decir exactamente lo
/// mismo y avisar una sola vez: mientras cada una lo armaba por su cuenta, tres mensajes seguidos del
/// mismo postulante producian tres avisos identicos al analista (AL9, P4).
/// </para>
/// <para>
/// El aviso es generico a proposito: dice con que cuentas coincide, nunca que escribio la persona en
/// las conversaciones de las otras (Regla 4).
/// </para>
/// </summary>
public static class AvisoMultiCuenta
{
    /// <summary>
    /// Avisos que corresponden en esta evaluacion.
    /// <para>
    /// <paramref name="notificarOtrasCuentas"/> avisa tambien a los analistas de las otras cuentas
    /// vivas: es lo que corresponde cuando nace una postulacion (JobForms completado), porque para
    /// ellos la novedad es que su postulante ahora tambien esta en otra empresa.
    /// </para>
    /// </summary>
    public static IEnumerable<AccionRegla> Acciones(
        ContextoRegla ctx, int analistaDestinoId, bool notificarOtrasCuentas = false)
    {
        var cuentaActual = ctx.Cuenta?.CuentaId;

        var otras = ctx.PostulacionesDelPostulante
            .Where(p => p.Estado is EstadoPostulacion.EnProceso or EstadoPostulacion.Reingreso)
            .Where(p => p.CuentaId != cuentaActual)
            .ToList();

        if (otras.Count == 0)
            yield break;

        var nombres = string.Join(", ", otras.Select(p => p.Cuenta).Distinct());

        yield return new NotificarAnalista(
            analistaDestinoId,
            $"Este postulante tambien esta en proceso con: {nombres}.");

        if (!notificarOtrasCuentas)
            yield break;

        var propia = ctx.Cuenta?.Nombre ?? "otra empresa";

        // Un aviso por analista, no por postulacion: dos vacantes vivas de la misma cuenta son el
        // mismo analista y el mismo dato.
        var destinos = otras
            .Select(p => p.AnalistaAsignadoId)
            .Where(id => id is not null && id != analistaDestinoId)
            .Select(id => id!.Value)
            .Distinct();

        foreach (var destino in destinos)
        {
            yield return new NotificarAnalista(
                destino,
                $"Tu postulante tambien esta en proceso con: {propia}.");
        }
    }
}
