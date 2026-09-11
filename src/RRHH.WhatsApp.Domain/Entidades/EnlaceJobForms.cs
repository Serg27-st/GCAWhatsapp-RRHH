namespace RRHH.WhatsApp.Domain.Entidades;

/// <summary>
/// Convencion del enlace que se le manda al postulante. Vive aparte porque la arman dos lugares
/// distintos —el servicio de invitaciones al crearla y la fabrica de contexto al recordarla— y
/// tienen que producir exactamente la misma URL.
/// </summary>
public static class EnlaceJobForms
{
    /// <summary>Parametro por el que viaja el token en la URL.</summary>
    public const string Parametro = "t";

    /// <summary>
    /// Devuelve nulo si la vacante no tiene formulario configurado: es preferible omitir el envio
    /// a mandarle al postulante un enlace roto (Regla 20 en espiritu).
    /// <para>
    /// Viaja el token y no el HcId, que es secuencial y permitiria enumerar vacantes de otras
    /// cuentas cambiando el numero en la URL (Seccion 9.6.1).
    /// </para>
    /// </summary>
    public static string? Construir(string? urlBase, Guid token)
    {
        if (string.IsNullOrWhiteSpace(urlBase))
            return null;

        var separador = urlBase.Contains('?') ? '&' : '?';

        return $"{urlBase}{separador}{Parametro}={token:N}";
    }
}
