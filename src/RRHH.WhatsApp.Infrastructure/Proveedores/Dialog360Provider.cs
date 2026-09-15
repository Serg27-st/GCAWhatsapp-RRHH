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
/// Adaptador de 360dialog. Traduce entre el dominio y la Cloud API que 360dialog expone como BSP.
/// Ningun tipo propio del proveedor sale de esta clase: si manana se cambia a Meta Cloud API
/// directa, se reemplaza esta implementacion y nada mas.
/// </summary>
public sealed class Dialog360Provider(
    HttpClient http,
    IOptions<Dialog360Opciones> opciones,
    LimitadorEnvio limitador,
    TimeProvider reloj,
    ILogger<Dialog360Provider> log) : IWhatsAppProvider
{
    /// <summary>Cabecera con la que 360dialog firma el cuerpo del webhook.</summary>
    public const string CabeceraFirma = "x-360dialog-signature";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Dialog360Opciones _opciones = opciones.Value;

    public string Nombre => "360dialog";

    // ---------------------------------------------------------------- entrada

    /// <summary>
    /// Verifica el HMAC-SHA256 del cuerpo crudo contra el secreto de plataforma.
    /// <para>
    /// Falla cerrado: sin secreto configurado no se puede validar nada y por lo tanto no se acepta
    /// nada. El cuerpo debe llegar exactamente como vino por la red — si se deserializa y se
    /// vuelve a serializar, la firma deja de coincidir.
    /// </para>
    /// </summary>
    public bool ValidarFirma(string cuerpoCrudo, IReadOnlyDictionary<string, string> cabeceras)
    {
        if (string.IsNullOrWhiteSpace(_opciones.SecretoWebhook))
        {
            log.LogError("Se recibio un webhook pero no hay secreto configurado. Se rechaza.");
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

        // Algunas pasarelas prefijan el algoritmo, al estilo de Meta ("sha256=...").
        if (recibida.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            recibida = recibida["sha256=".Length..];

        var esperada = CalcularFirma(cuerpoCrudo, _opciones.SecretoWebhook);

        // Comparacion de tiempo fijo: una comparacion normal filtra el secreto por lo que tarda.
        var iguales = CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(esperada),
            Encoding.ASCII.GetBytes(recibida.Trim().ToLowerInvariant()));

        if (!iguales)
            log.LogWarning("Firma de webhook invalida. Se rechaza el payload.");

        return iguales;
    }

    public static string CalcularFirma(string cuerpoCrudo, string secreto)
    {
        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secreto),
            Encoding.UTF8.GetBytes(cuerpoCrudo));

        return Convert.ToHexStringLower(hash);
    }

    public IReadOnlyList<MensajeEntranteDto> InterpretarWebhook(string cuerpoCrudo) =>
        InterpreteWebhookMeta.Mensajes(cuerpoCrudo, reloj.GetUtcNow().UtcDateTime, log);

    public IReadOnlyList<EstadoEntregaDto> InterpretarEstados(string cuerpoCrudo) =>
        InterpreteWebhookMeta.Estados(cuerpoCrudo, reloj.GetUtcNow().UtcDateTime, log);

    // ----------------------------------------------------------------- salida

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
        if (!_opciones.EstaConfigurado)
            return ResultadoEnvio.Permanente("El proveedor no tiene API key configurada.");

        // Ningun envio se salta el limitador (Seccion 9.6.4).
        await limitador.EsperarTurnoAsync(ct);

        try
        {
            using var respuesta = await http.PostAsJsonAsync("messages", cuerpo, Json, ct);
            var texto = await respuesta.Content.ReadAsStringAsync(ct);

            if (!respuesta.IsSuccessStatusCode)
            {
                log.LogError("360dialog rechazo el envio: {Codigo} {Cuerpo}", (int)respuesta.StatusCode, texto);

                var error = $"HTTP {(int)respuesta.StatusCode}: {texto}";

                // Solo lo que no llego a procesarse se reintenta. Un 401 o un 400 dan siempre el
                // mismo resultado y reintentarlos solo gasta cuota.
                return respuesta.StatusCode is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError
                    ? ResultadoEnvio.Transitorio(error)
                    : ResultadoEnvio.Permanente(error);
            }

            return ResultadoEnvio.Ok(LeerIdMensaje(texto));
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Se perdio la respuesta: el mensaje pudo haber salido. Reintentarlo podria duplicarlo.
            log.LogError(ex, "Se perdio la respuesta de 360dialog. El envio queda como ambiguo.");
            return ResultadoEnvio.Ambiguo($"Sin respuesta dentro del tiempo limite: {ex.Message}");
        }
        catch (HttpRequestException ex) when (ClasificadorFallosHttp.NuncaLlegoASalir(ex))
        {
            // Fallo de conexion (DNS, TCP o TLS): la peticion nunca llego, asi que reintentar es
            // seguro. Es el unico caso Transitorio de una excepcion (ARQ-04/C4).
            log.LogError(ex, "No se pudo conectar con 360dialog.");
            return ResultadoEnvio.Transitorio(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            // Cualquier otro HttpRequestException pudo pasar con la peticion ya en vuelo: no se
            // sabe si 360dialog la proceso, asi que no se reintenta solo (V29).
            log.LogError(ex, "Fallo la peticion a 360dialog despues de enviarla. El envio queda como ambiguo.");
            return ResultadoEnvio.Ambiguo(ex.Message);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Sin AddStandardResilienceHandler() (ARQ-04/C4), nada atraviesa el adaptador sin
            // clasificar: cualquier otra excepcion pudo ocurrir con la peticion ya en vuelo.
            log.LogError(ex, "Fallo inesperado enviando a 360dialog. El envio queda como ambiguo.");
            return ResultadoEnvio.Ambiguo(ex.Message);
        }
    }

    private static string? LeerIdMensaje(string respuestaJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(respuestaJson);
            return doc.RootElement.TryGetProperty("messages", out var mensajes)
                   && mensajes.ValueKind == JsonValueKind.Array
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

}
