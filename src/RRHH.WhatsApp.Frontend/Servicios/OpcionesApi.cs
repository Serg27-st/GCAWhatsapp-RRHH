namespace RRHH.WhatsApp.Frontend.Servicios;

/// <summary>
/// Donde vive la Api. El Frontend no conoce la base de datos: todo lo que muestra pasa por HTTP
/// (limite del CLAUDE.md y desviacion V7 de docs/decisiones.md).
/// </summary>
public sealed class OpcionesApi
{
    public const string Seccion = "Api";

    public string BaseUrl { get; set; } = "http://localhost:5087";

    /// <summary>Cada cuanto la bandeja vuelve a pedir la lista mientras no exista SignalR.</summary>
    public int RefrescoSegundos { get; set; } = 20;
}
