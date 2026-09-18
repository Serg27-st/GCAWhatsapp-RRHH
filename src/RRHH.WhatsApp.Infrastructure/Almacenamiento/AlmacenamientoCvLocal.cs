using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Infrastructure.Almacenamiento;

/// <summary>
/// Guarda los CVs en el sistema de archivos, que en el despliegue on-premise es un recurso
/// compartido de la empresa. La interfaz deja la puerta abierta a mover los archivos a
/// almacenamiento de objetos sin tocar el resto del sistema (Seccion 8.3).
/// <para>
/// El circuito —cuarentena, antivirus, tope— es el de <see cref="AlmacenamientoArchivosLocal"/>, el
/// mismo que usan los adjuntos de WhatsApp (V33).
/// </para>
/// </summary>
public sealed class AlmacenamientoCvLocal(
    IOptions<OpcionesCv> opciones,
    IEscanerAntivirus antivirus,
    TimeProvider reloj,
    ILogger<AlmacenamientoCvLocal> log)
    : AlmacenamientoArchivosLocal(PoliticaDe(opciones.Value), antivirus, reloj, log), IAlmacenamientoCv
{
    private static Politica PoliticaDe(OpcionesCv cv) => new(
        cv.Carpeta,
        cv.TamanoMaximoMb,
        cv.ExtensionesPermitidas,
        cv.Antivirus.ExigirEscaneo,
        "CV");

    public async Task<string> GuardarAsync(
        Stream contenido, string nombreArchivo, string contentType, CancellationToken ct = default)
    {
        // El formulario siempre trae nombre: es lo que decide la extension.
        var extension = Path.GetExtension(nombreArchivo).ToLowerInvariant();

        return (await GuardarArchivoAsync(contenido, extension, ct)).Ruta;
    }
}
