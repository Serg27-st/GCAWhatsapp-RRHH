using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Infrastructure.Proveedores;

/// <summary>
/// Habla directo con la Cloud API de Meta (Sección 9.3, patrón Adapter).
/// <para>
/// Comparte con 360dialog los cuerpos de mensaje y el intérprete del webhook, porque 360dialog es
/// un paso a través de esta misma API. Lo que cambia es de dónde cuelga la URL, cómo se
/// autentica —Bearer en vez de cabecera propia— y cómo viene firmado el webhook.
/// </para>
/// </summary>
public sealed class MetaCloudProvider(
    HttpClient http,
    IOptions<OpcionesMetaCloud> opciones,
    LimitadorEnvio limitador,
    ILogger<MetaCloudProvider> log) : IWhatsAppProvider
{
    /// <summary>Cabecera con la que Meta firma el cuerpo del webhook.</summary>
    public const string CabeceraFirma = "X-Hub-Signature-256";

    /// <summary>Meta antepone el algoritmo a la firma.</summary>
    private const string Prefijo = "sha256=";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly OpcionesMetaCloud _opciones = opciones.Value;

    public string Nombre => "Meta Cloud API";

    /// <summary>
    /// Verifica que el payload venga de Meta y no de cualquiera que conozca la URL.
    /// <para>
    /// Sin secreto configurado se rechaza todo. Es deliberado: el webhook es público, y aceptar
    /// mensajes que no se pueden atribuir es peor que no recibir ninguno.
    /// </para>
    /// </summary>
    public bool ValidarFirma(string cuerpoCrudo, IReadOnlyDictionary<string, string> cabeceras)
    {
        if (string.IsNullOrWhiteSpace(_opciones.AppSecret))
        {
            log.LogError("No hay AppSecret configurado: no se puede verificar el webhook. Se rechaza.");
            return false;
        }

        var recibida = cabeceras
            .FirstOrDefault(c => string.Equals(c.Key, CabeceraFirma, StringComparison.OrdinalIgnoreCase))
            .Value;

        if (string.IsNullOrWhiteSpace(recibida))
        {
            log.LogWarning("Webhook sin cabecera {Cabecera}. Se rechaza.", CabeceraFirma);
            return false;
        }

        var esperada = Prefijo + CalcularFirma(cuerpoCrudo, _opciones.AppSecret);

        // Comparación de tiempo fijo: una comparación normal filtra la firma por el reloj.
        var iguales = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(recibida.Trim()),
            Encoding.UTF8.GetBytes(esperada));

        if (!iguales)
            log.LogWarning("La firma del webhook no coincide. Se rechaza.");

        return iguales;
    }

    /// <summary>HMAC-SHA256 del cuerpo crudo, en hexadecimal minúscula, como lo calcula Meta.</summary>
    public static string CalcularFirma(string cuerpoCrudo, string secreto)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secreto));

        return Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(cuerpoCrudo)));
    }

    public IReadOnlyList<MensajeEntranteDto> InterpretarWebhook(string cuerpoCrudo) =>
        InterpreteWebhookMeta.Mensajes(cuerpoCrudo, log);

    public IReadOnlyList<EstadoEntregaDto> InterpretarEstados(string cuerpoCrudo) =>
        InterpreteWebhookMeta.Estados(cuerpoCrudo, log);

    public Task<ResultadoEnvio> EnviarTextoAsync(
        string telefonoE164, string texto, CancellationToken ct = default) =>
        EnviarAsync(CuerposMensaje.Texto(telefonoE164, texto), ct);

    public Task<ResultadoEnvio> EnviarPlantillaAsync(
        string telefonoE164, Plantilla plantilla, IReadOnlyList<string> parametros, CancellationToken ct = default)
    {
        if (CuerposMensaje.ValidarPlantilla(plantilla, parametros) is { } motivo)
            // Plantilla sin aprobar o con la cantidad de parametros equivocada: no cambia reintentando.
            return Task.FromResult(ResultadoEnvio.Permanente(motivo));

        return EnviarAsync(CuerposMensaje.Plantilla(telefonoE164, plantilla, parametros), ct);
    }

    public Task<ResultadoEnvio> EnviarBotonesAsync(
        string telefonoE164, string texto, IReadOnlyList<BotonRespuesta> botones, CancellationToken ct = default) =>
        EnviarAsync(CuerposMensaje.Botones(telefonoE164, texto, botones), ct);

    public Task<ResultadoEnvio> EnviarListaAsync(
        string telefonoE164, string texto, string textoBoton,
        IReadOnlyList<BotonRespuesta> opciones, CancellationToken ct = default) =>
        EnviarAsync(CuerposMensaje.Lista(telefonoE164, texto, textoBoton, opciones), ct);

    private async Task<ResultadoEnvio> EnviarAsync(object cuerpo, CancellationToken ct)
    {
        // Sección 9.6.4: el saliente va limitado. Es el patrón de uso que provocó el bloqueo
        // original, así que se controla en el adaptador y no en cada llamador.
        await limitador.EsperarTurnoAsync(ct);

        try
        {
            var ruta = $"{_opciones.Version}/{_opciones.PhoneNumberId}/messages";

            using var respuesta = await http.PostAsJsonAsync(ruta, cuerpo, Json, ct);

            var texto = await respuesta.Content.ReadAsStringAsync(ct);

            if (!respuesta.IsSuccessStatusCode)
            {
                log.LogError("Meta rechazo el envio ({Codigo}): {Cuerpo}",
                    (int)respuesta.StatusCode, texto);

                var error = LeerError(texto) ?? texto;

                return Clasificar(respuesta.StatusCode) switch
                {
                    ClaseFallo.Transitorio => ResultadoEnvio.Transitorio(error),
                    _ => ResultadoEnvio.Permanente(error)
                };
            }

            return ResultadoEnvio.Ok(LeerIdMensaje(texto));
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Se agoto el tiempo esperando la respuesta. Meta pudo haber aceptado el mensaje y
            // haberse perdido solo el acuse: reintentar podria entregarlo dos veces.
            log.LogError(ex, "Se perdio la respuesta de Meta. El envio queda como ambiguo.");

            return ResultadoEnvio.Ambiguo($"Sin respuesta de Meta dentro del tiempo limite: {ex.Message}");
        }
        catch (HttpRequestException ex) when (ClasificadorFallosHttp.NuncaLlegoASalir(ex))
        {
            // Fallo de conexion (DNS, TCP o TLS): la peticion nunca llego a procesarse, asi que
            // reintentar es seguro. Es el unico caso Transitorio de una excepcion (ARQ-04/C4).
            log.LogError(ex, "No se pudo conectar con la Cloud API de Meta.");

            return ResultadoEnvio.Transitorio(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            // Cualquier otro HttpRequestException (la conexion se corto a mitad de respuesta, el
            // protocolo vino roto) pudo pasar despues de que la peticion ya viajo a Meta. No se
            // sabe si la proceso, asi que no se reintenta solo (V29): ReintentoEnvios solo toma los
            // Transitorios, y un Ambiguo lo resuelve una persona.
            log.LogError(ex, "Fallo la peticion a la Cloud API de Meta despues de enviarla. El envio queda como ambiguo.");

            return ResultadoEnvio.Ambiguo(ex.Message);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Sin AddStandardResilienceHandler() (ARQ-04/C4), nada atraviesa el adaptador sin
            // clasificar: cualquier otra excepcion —serializacion, IO— pudo ocurrir con la
            // peticion ya en vuelo, asi que se trata igual que un HttpRequestException tardio.
            log.LogError(ex, "Fallo inesperado enviando a la Cloud API de Meta. El envio queda como ambiguo.");

            return ResultadoEnvio.Ambiguo(ex.Message);
        }
    }

    /// <summary>
    /// Solo lo que Meta no llego a procesar es reintentable. Un 401 o un 400 dan el mismo
    /// resultado siempre y reintentarlos solo gasta cuota.
    /// </summary>
    private static ClaseFallo Clasificar(HttpStatusCode codigo) =>
        codigo is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError
            ? ClaseFallo.Transitorio
            : ClaseFallo.Permanente;

    /// <summary>El id con el que Meta identifica el mensaje; es la clave de los acuses de entrega.</summary>
    private static string? LeerIdMensaje(string respuestaJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(respuestaJson);

            return doc.RootElement.TryGetProperty("messages", out var mensajes)
                && mensajes.GetArrayLength() > 0
                && mensajes[0].TryGetProperty("id", out var id)
                    ? id.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Meta devuelve el detalle en <c>error.message</c>. Rescatarlo evita que el analista vea un
    /// JSON crudo cuando el envio falla.
    /// </summary>
    private static string? LeerError(string respuestaJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(respuestaJson);

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
