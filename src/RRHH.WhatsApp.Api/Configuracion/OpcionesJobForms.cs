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

    /// <summary>
    /// Solicitudes por minuto y por IP en los endpoints publicos. Tambien es el tope de
    /// <c>webhook-google</c> cuando la solicitud no trae el secreto correcto (Politicas
    /// LimiteWebhookGoogle): sin poder distinguir al script legitimo de un desconocido, cae al
    /// mismo cupo que el resto.
    /// </summary>
    public int LimitePorMinuto { get; set; } = 30;

    /// <summary>
    /// Solicitudes por minuto para quien SI trae el secreto valido en <c>webhook-google</c>
    /// (COR-15). Bien por encima del tope publico porque esta particion la comparten todas las
    /// llamadas legitimas del Apps Script, sin importar de que IP salgan.
    /// </summary>
    public int LimitePorMinutoWebhook { get; set; } = 600;

    /// <summary>
    /// Dominios a los que puede apuntar <c>CvUrl</c> (COR-15/M8). El analista termina abriendo ese
    /// enlace a ciegas, asi que no alcanza con "es una URL": tiene que ser uno de los que la
    /// empresa decidio confiar, con coincidencia exacta de host — ver
    /// <see cref="RRHH.WhatsApp.Api.Configuracion.ValidadorCvUrl"/>.
    /// <para>
    /// Nula a proposito, no inicializada con <see cref="DominiosCvPermitidosPorDefecto"/>: el
    /// binder de configuracion de .NET, cuando la propiedad ya trae un array con datos, AGREGA los
    /// indices que encuentra en <c>appsettings.json</c> en vez de reemplazarlos. Si el default
    /// viviera aca, cargar la seccion sumaria (o mezclaria) los dominios en vez de reemplazarlos.
    /// <see cref="DominiosCvPermitidosEfectivos"/> es la que hay que leer siempre.
    /// </para>
    /// </summary>
    public string[]? DominiosCvPermitidos { get; set; }

    /// <summary>Los dos dominios de Google que usa el JobForms mientras viva en Google Forms (D3).</summary>
    public static readonly string[] DominiosCvPermitidosPorDefecto =
        ["drive.google.com", "docs.google.com"];

    /// <summary>
    /// Lo que hay que validar de verdad: lo configurado si trae algo, o el default si la seccion
    /// vino vacia o sin esta clave.
    /// </summary>
    public string[] DominiosCvPermitidosEfectivos =>
        DominiosCvPermitidos is { Length: > 0 } ? DominiosCvPermitidos : DominiosCvPermitidosPorDefecto;

    public bool EstaConfigurado => !string.IsNullOrWhiteSpace(SecretoWebhook);
}
