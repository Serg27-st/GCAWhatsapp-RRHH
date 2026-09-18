using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Infrastructure.Proveedores;

/// <summary>
/// Los dos pasos para bajar un archivo de la Cloud API (V33, ARQ-10), comunes a Meta y a 360dialog:
/// pedir los datos del medio —que traen una URL valida por unos minutos— y bajar el contenido.
/// <para>
/// Lo que cambia entre proveedores es la ruta del primer paso y a donde se pide el segundo: Meta baja
/// de su CDN con el token, 360dialog pide la misma ruta a su propio host con su clave. Las credenciales
/// las pone el <see cref="HttpClient"/> de cada adaptador como cabecera por defecto.
/// </para>
/// <para>
/// La URL de descarga es confidencial mientras vale (lo dice 360dialog): no se escribe en el log.
/// </para>
/// </summary>
internal static class DescargaMedioHttp
{
    /// <param name="rutaMedio">Relativa al <c>BaseAddress</c> del cliente.</param>
    /// <param name="destino">
    /// Traduce la URL que devolvio el proveedor a la que hay que pedir. Nula si no se puede pedir sin
    /// exponer las credenciales.
    /// </param>
    public static async Task<ResultadoDescarga> DescargarAsync(
        HttpClient http,
        string rutaMedio,
        Func<Uri, Uri?> destino,
        string proveedorMedioId,
        ILogger log,
        CancellationToken ct)
    {
        DatosMedio datos;

        try
        {
            using var respuesta = await http.GetAsync(rutaMedio, ct);
            var texto = await respuesta.Content.ReadAsStringAsync(ct);

            if (!respuesta.IsSuccessStatusCode)
                return Rechazo(respuesta.StatusCode, "al pedir los datos del medio", texto, proveedorMedioId, log);

            if (Leer(texto) is not { } leidos)
            {
                log.LogWarning("El proveedor no devolvio una URL para el medio {MedioId}.", proveedorMedioId);
                return ResultadoDescarga.Permanente("El proveedor no devolvio una URL para el medio.");
            }

            datos = leidos;
        }
        catch (Exception ex) when (EsFalloDeRed(ex, ct))
        {
            log.LogWarning(ex, "No se pudieron pedir los datos del medio {MedioId}.", proveedorMedioId);
            return ResultadoDescarga.Transitorio($"Sin respuesta del proveedor al pedir los datos del medio: {ex.Message}");
        }

        if (destino(datos.Url) is not { } url)
        {
            // El token no viaja a una direccion que no se pueda verificar ni por una conexion sin cifrar.
            log.LogError("La URL del medio {MedioId} no es segura. No se pide.", proveedorMedioId);
            return ResultadoDescarga.Permanente("El proveedor devolvio una URL de descarga que no es segura.");
        }

        HttpResponseMessage? archivo = null;

        try
        {
            // Sin leer el cuerpo: un documento puede pesar decenas de megas. Quien recibe el flujo lo
            // copia a disco con su propio tope (V33).
            archivo = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!archivo.IsSuccessStatusCode)
            {
                // La URL vale unos minutos, y un 404 o un 403 aca suele ser eso: que vencio. El proximo
                // intento vuelve a pedir los datos del medio, y es esa respuesta la que dice si el
                // archivo sigue existiendo.
                log.LogWarning("No se pudo bajar el archivo del medio {MedioId} ({Codigo}).",
                    proveedorMedioId, (int)archivo.StatusCode);

                return ResultadoDescarga.Transitorio(
                    $"HTTP {(int)archivo.StatusCode} al bajar el archivo: se vuelve a pedir la URL en el proximo intento.");
            }

            var flujo = await archivo.Content.ReadAsStreamAsync(ct);

            var medio = new MedioDescargado(
                new FlujoConRespuesta(flujo, archivo),
                datos.MimeType ?? archivo.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
                datos.Tamano ?? archivo.Content.Headers.ContentLength);

            // Desde aca la respuesta la cierra el flujo, cuando quien lo recibe termine de leerlo.
            archivo = null;

            return ResultadoDescarga.Ok(medio);
        }
        catch (Exception ex) when (EsFalloDeRed(ex, ct))
        {
            log.LogWarning(ex, "No se pudo bajar el medio {MedioId}.", proveedorMedioId);
            return ResultadoDescarga.Transitorio($"Se corto la descarga del archivo: {ex.Message}");
        }
        finally
        {
            archivo?.Dispose();
        }
    }

    /// <summary>
    /// Pedir un archivo no cambia nada del proveedor, asi que cualquier corte de red o tiempo agotado se
    /// puede reintentar: aca no hay fallo ambiguo como en los envios (V29). La cancelacion de quien
    /// llama no es un fallo y sigue su curso.
    /// </summary>
    private static bool EsFalloDeRed(Exception ex, CancellationToken ct) =>
        !ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or IOException;

    /// <summary>
    /// Lo que el proveedor no llego a atender se reintenta. Un 4xx —id vencido o inexistente, sin
    /// permiso— da siempre lo mismo.
    /// </summary>
    private static ResultadoDescarga Rechazo(
        HttpStatusCode codigo, string paso, string cuerpo, string proveedorMedioId, ILogger log)
    {
        var error = $"HTTP {(int)codigo} {paso}: {LeerError(cuerpo) ?? "sin detalle"}";

        var transitorio = codigo is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout
            || (int)codigo >= 500;

        log.LogWarning("Fallo la descarga del medio {MedioId} ({Codigo}, {Clase}).",
            proveedorMedioId, (int)codigo, transitorio ? "transitorio" : "permanente");

        return transitorio ? ResultadoDescarga.Transitorio(error) : ResultadoDescarga.Permanente(error);
    }

    private sealed record DatosMedio(Uri Url, string? MimeType, long? Tamano);

    /// <summary>
    /// <c>{ "url", "mime_type", "file_size", ... }</c>. Meta documenta <c>file_size</c> como texto y lo
    /// manda como numero: se aceptan los dos.
    /// </summary>
    private static DatosMedio? Leer(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var raiz = doc.RootElement;

            if (!raiz.TryGetProperty("url", out var url)
                || url.ValueKind != JsonValueKind.String
                || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var direccion))
            {
                return null;
            }

            var mime = raiz.TryGetProperty("mime_type", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString()
                : null;

            long? tamano = null;

            if (raiz.TryGetProperty("file_size", out var t))
            {
                if (t.ValueKind == JsonValueKind.Number && t.TryGetInt64(out var numero))
                    tamano = numero;
                else if (t.ValueKind == JsonValueKind.String && long.TryParse(t.GetString(), out var texto))
                    tamano = texto;
            }

            return new DatosMedio(direccion, string.IsNullOrWhiteSpace(mime) ? null : mime, tamano);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? LeerError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);

            return doc.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var mensaje)
                    ? mensaje.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// El contenido de una respuesta HTTP que se entrega sin leer. Cerrarlo cierra tambien la respuesta, y
/// con ella la conexion: sin esto, cada descarga dejaria una conexion colgada hasta el recolector.
/// </summary>
internal sealed class FlujoConRespuesta(Stream interno, HttpResponseMessage respuesta) : Stream
{
    public override bool CanRead => interno.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => interno.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => interno.Read(buffer);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        interno.ReadAsync(buffer, offset, count, cancellationToken);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        interno.ReadAsync(buffer, cancellationToken);

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            interno.Dispose();
            respuesta.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await interno.DisposeAsync();
        respuesta.Dispose();

        await base.DisposeAsync();
    }
}
