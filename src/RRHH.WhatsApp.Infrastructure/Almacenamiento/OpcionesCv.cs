namespace RRHH.WhatsApp.Infrastructure.Almacenamiento;

/// <summary>
/// Donde viven los CVs y que se acepta de ellos. Fuera de SQL Server (Seccion 8.3): en el
/// despliegue on-premise es un recurso compartido de la empresa, no la base.
/// </summary>
public sealed class OpcionesCv
{
    public const string Seccion = "Cv";

    /// <summary>Carpeta raiz. En produccion, una ruta UNC del recurso compartido.</summary>
    public string Carpeta { get; set; } = "cv";

    /// <summary>Tope de tamano. El endpoint publico es el unico accesible desde internet sin login.</summary>
    public int TamanoMaximoMb { get; set; } = 10;

    /// <summary>Extensiones admitidas, en minuscula y con punto.</summary>
    public string[] ExtensionesPermitidas { get; set; } = [".pdf", ".doc", ".docx"];

    /// <summary>Escaneo antivirus del adjunto (Seccion 9.6.1).</summary>
    public OpcionesAntivirus Antivirus { get; set; } = new();
}
