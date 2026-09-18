namespace RRHH.WhatsApp.Infrastructure.Almacenamiento;

/// <summary>
/// Donde viven los archivos que mandan los postulantes por WhatsApp y que se acepta de ellos (V33).
/// El antivirus es el mismo que el del CV (<c>Cv:Antivirus</c>).
/// </summary>
public sealed class OpcionesAdjuntos
{
    public const string Seccion = "Adjuntos";

    /// <summary>
    /// Carpeta raiz. Vacia: la subcarpeta <c>adjuntos</c> de <c>Cv:Carpeta</c>, que ya esta en el
    /// recurso compartido, en los respaldos y bajo los mismos permisos que los CVs.
    /// </summary>
    public string Carpeta { get; set; } = string.Empty;

    /// <summary>
    /// Tope de tamaño. WhatsApp admite hasta 16 MB en audio y video; un documento mas grande casi nunca
    /// es un CV.
    /// </summary>
    public int TamanoMaximoMb { get; set; } = 16;

    /// <summary>
    /// Extensiones admitidas, en minuscula y con punto: documentos del CV, fotos (el DNI suele llegar
    /// asi), notas de voz y video. Lo ejecutable nunca.
    /// </summary>
    public string[] ExtensionesPermitidas { get; set; } =
    [
        ".pdf", ".doc", ".docx",
        ".jpg", ".jpeg", ".png", ".webp",
        ".ogg", ".opus", ".mp3", ".m4a", ".aac", ".amr",
        ".mp4", ".3gp"
    ];

    /// <summary>La carpeta efectiva, resuelta contra la de los CVs cuando no se configuro una propia.</summary>
    public string CarpetaEfectiva(OpcionesCv cv) =>
        string.IsNullOrWhiteSpace(Carpeta) ? Path.Combine(cv.Carpeta, "adjuntos") : Carpeta;
}
