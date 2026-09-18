using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Domain.Excepciones;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Infrastructure.Almacenamiento;

/// <summary>
/// Guarda los archivos que mandan los postulantes por WhatsApp (V33, ARQ-10) con el mismo circuito que
/// el CV: cuarentena, antivirus, tope y extensiones permitidas.
/// <para>
/// Lo que cambia es de donde sale la extension: WhatsApp solo manda nombre con los documentos, asi que
/// una foto o una nota de voz la toman de su tipo. El nombre que puso la persona nunca forma parte de
/// la ruta.
/// </para>
/// </summary>
public sealed class AlmacenamientoAdjuntosLocal(
    IOptions<OpcionesAdjuntos> opciones,
    IOptions<OpcionesCv> cv,
    IEscanerAntivirus antivirus,
    TimeProvider reloj,
    ILogger<AlmacenamientoAdjuntosLocal> log)
    : AlmacenamientoArchivosLocal(PoliticaDe(opciones.Value, cv.Value), antivirus, reloj, log), IAlmacenamientoAdjuntos
{
    /// <summary>Los tipos que manda WhatsApp, con la extension con la que se guardan.</summary>
    private static readonly Dictionary<string, string> ExtensionPorTipo = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = ".pdf",
        ["application/msword"] = ".doc",
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ".docx",
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["audio/ogg"] = ".ogg",
        ["audio/opus"] = ".opus",
        ["audio/mpeg"] = ".mp3",
        ["audio/mp4"] = ".m4a",
        ["audio/aac"] = ".aac",
        ["audio/amr"] = ".amr",
        ["video/mp4"] = ".mp4",
        ["video/3gpp"] = ".3gp"
    };

    private static Politica PoliticaDe(OpcionesAdjuntos adjuntos, OpcionesCv cv) => new(
        adjuntos.CarpetaEfectiva(cv),
        adjuntos.TamanoMaximoMb,
        adjuntos.ExtensionesPermitidas,
        cv.Antivirus.ExigirEscaneo,
        "adjunto");

    protected override Exception Rechazo(string motivo) => new ArchivoRechazadoException(motivo);

    public Task<ArchivoGuardado> GuardarAsync(
        Stream contenido, string? nombreArchivo, string mimeType, CancellationToken ct = default) =>
        GuardarArchivoAsync(contenido, Extension(nombreArchivo, mimeType), ct);

    /// <summary>
    /// La del nombre, si trae una; si no, la del tipo. Un nombre con extension manda aunque el tipo diga
    /// otra cosa: <c>cv.pdf.exe</c> se rechaza por <c>.exe</c>, que es como lo abriria la maquina del
    /// analista.
    /// </summary>
    private static string? Extension(string? nombreArchivo, string mimeType)
    {
        if (!string.IsNullOrWhiteSpace(nombreArchivo) && Path.GetExtension(nombreArchivo) is { Length: > 1 } propia)
            return propia.ToLowerInvariant();

        // «audio/ogg; codecs=opus»: los parametros no cambian el tipo.
        var tipo = mimeType.Split(';', 2)[0].Trim();

        return ExtensionPorTipo.GetValueOrDefault(tipo);
    }
}
