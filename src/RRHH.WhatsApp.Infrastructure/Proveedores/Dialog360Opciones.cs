namespace RRHH.WhatsApp.Infrastructure.Proveedores;

/// <summary>
/// Configuracion del proveedor. Nada de esto se versiona: la clave y el secreto llegan por
/// variables de entorno por ambiente (Seccion 9.6.1).
/// </summary>
public sealed class Dialog360Opciones
{
    public const string Seccion = "Dialog360";

    /// <summary>Endpoint de la Cloud API de 360dialog. Se sobreescribe para apuntar al sandbox.</summary>
    public string BaseUrl { get; set; } = "https://waba-v2.360dialog.io";

    /// <summary>Se envia en la cabecera D360-API-KEY. Viene de variable de entorno.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Secreto con el que 360dialog firma el cuerpo del webhook (HMAC-SHA256, cabecera
    /// x-360dialog-signature). Sin este valor el webhook rechaza todo: preferimos no recibir
    /// nada antes que procesar un payload que no podemos verificar.
    /// </summary>
    public string SecretoWebhook { get; set; } = string.Empty;

    /// <summary>Tope de mensajes salientes por segundo. Se lee de ConfiguracionReglas y este valor es el respaldo.</summary>
    public int MaximoPorSegundo { get; set; } = 10;

    public int TimeoutSegundos { get; set; } = 10;

    public bool EstaConfigurado => !string.IsNullOrWhiteSpace(ApiKey);
}
