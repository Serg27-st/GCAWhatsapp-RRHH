namespace RRHH.WhatsApp.Domain.Entidades;

/// <summary>
/// Arma el enlace del formulario que el bot le manda al postulante (Regla 9).
/// </summary>
public static class EnlaceJobForms
{
    /// <summary>Parametro por el que viaja el token cuando la URL no dice donde ponerlo.</summary>
    public const string Parametro = "t";

    /// <summary>
    /// Marcador que la URL de la vacante puede traer para decir exactamente donde va el token.
    /// <para>
    /// Google Forms solo prellena parametros con la forma <c>entry.&lt;id&gt;=</c>, distinta en cada
    /// formulario. Con un <c>?t=</c> al final, Google lo ignora y el token nunca llega a la respuesta:
    /// el webhook recibiria un envio que no puede atribuir a ninguna postulacion. Por eso la URL de
    /// la vacante se guarda con el marcador donde corresponda (V27).
    /// </para>
    /// </summary>
    public const string Marcador = "{token}";

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

        if (urlBase.Contains(Marcador, StringComparison.OrdinalIgnoreCase))
            return urlBase.Replace(Marcador, token.ToString("N"), StringComparison.OrdinalIgnoreCase);

        // Sin marcador se agrega el parametro propio. Es lo que sirve para el formulario propio del
        // dia que se migre a Razor Pages (D3), que lee el token de la query.
        var separador = urlBase.Contains('?') ? '&' : '?';

        return $"{urlBase}{separador}{Parametro}={token:N}";
    }
}
