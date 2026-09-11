namespace RRHH.WhatsApp.Api.Configuracion;

/// <summary>
/// Nombres de las politicas de limite de velocidad. Constantes para que el registro en Program y
/// el atributo del controlador no se separen por una cadena mal escrita.
/// </summary>
public static class PoliticasLimite
{
    /// <summary>
    /// Endpoints del JobForms alcanzables desde internet sin autenticacion. Sin limite quedan
    /// expuestos a abuso automatizado (Seccion 9.6.1).
    /// </summary>
    public const string Publico = "publico";
}
