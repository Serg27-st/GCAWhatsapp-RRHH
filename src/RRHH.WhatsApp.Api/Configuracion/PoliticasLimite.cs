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

    /// <summary>
    /// Solo <c>POST /jobforms/webhook-google</c>. Particionada por secreto valido y no por IP
    /// (COR-15): las IPs de salida de Apps Script son compartidas entre scripts de distintos
    /// clientes de Google, asi que un limite por IP puede agotarse por trafico ajeno y dejar fuera
    /// al script legitimo justo en una campana con muchos envios simultaneos.
    /// </summary>
    public const string WebhookGoogle = "webhook-google";
}
