namespace RRHH.WhatsApp.Infrastructure.Proveedores;

/// <summary>
/// Conexión directa a la Cloud API de Meta.
/// <para>
/// Es el camino con número de prueba gratuito: Meta lo entrega al crear la app en
/// developers.facebook.com, sin costo y sin BSP de por medio. Sirve para probar el flujo completo
/// antes de contratar 360dialog (decisión D2), y el adaptador queda para elegir cuál usar.
/// </para>
/// </summary>
public sealed class OpcionesMetaCloud
{
    public const string Seccion = "MetaCloud";

    /// <summary>Versión de la Graph API. Meta la mantiene unos dos años; conviene fijarla.</summary>
    public string Version { get; set; } = "v25.0";

    /// <summary>
    /// Id del número emisor, no el número en sí. Aparece en la app de Meta, en WhatsApp > API Setup
    /// como "Phone number ID".
    /// </summary>
    public string PhoneNumberId { get; set; } = string.Empty;

    /// <summary>
    /// Token de acceso. El temporal de la consola dura 24 horas y sirve para probar; para dejarlo
    /// andando hace falta uno permanente de usuario del sistema. Llega por variable de entorno.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Secreto de la app (Configuración > Básica). Con él Meta firma el webhook en
    /// <c>X-Hub-Signature-256</c>. Sin esto no se puede verificar quién manda el payload.
    /// </summary>
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>
    /// Cadena que uno inventa y repite en la consola de Meta al dar de alta el webhook. Meta la
    /// devuelve en la verificación inicial para comprobar que el endpoint es de quien dice.
    /// </summary>
    public string TokenVerificacion { get; set; } = string.Empty;

    /// <summary>
    /// Tope de mensajes salientes por segundo (Sección 9.6.4). Es el respaldo: el valor que manda
    /// es <c>envio.maximo_por_segundo</c> en <c>ConfiguracionReglas</c>, leído por
    /// <see cref="ProveedorParametrosEnvio"/> con caché de 30 s (COR-12/AL8). Este solo se usa si
    /// la base no tiene el parámetro, no es válido, o no se puede leer.
    /// </summary>
    public int MaximoPorSegundo { get; set; } = 10;

    public int TimeoutSegundos { get; set; } = 10;

    public bool EstaConfigurado =>
        !string.IsNullOrWhiteSpace(AccessToken) && !string.IsNullOrWhiteSpace(PhoneNumberId);
}
