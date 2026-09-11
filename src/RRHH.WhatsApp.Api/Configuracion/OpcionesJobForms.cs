namespace RRHH.WhatsApp.Api.Configuracion;

/// <summary>
/// Configuracion del circuito publico del JobForms. El secreto llega por variable de entorno:
/// estos endpoints son los unicos accesibles desde internet sin autenticacion (Seccion 9.6.1).
/// </summary>
public sealed class OpcionesJobForms
{
    public const string Seccion = "JobForms";

    /// <summary>
    /// Secreto compartido con el Apps Script del formulario, que viaja en la cabecera
    /// <c>X-JobForms-Secreto</c>. Sin el configurado, el webhook rechaza todo: preferimos no
    /// recibir nada antes que aceptar un envio que no podemos atribuir.
    /// </summary>
    public string SecretoWebhook { get; set; } = string.Empty;

    /// <summary>Solicitudes por minuto y por IP en los endpoints publicos.</summary>
    public int LimitePorMinuto { get; set; } = 30;

    public bool EstaConfigurado => !string.IsNullOrWhiteSpace(SecretoWebhook);
}
