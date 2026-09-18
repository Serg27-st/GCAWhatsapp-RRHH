using System.Text.Json;
using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Infrastructure.Proveedores;

/// <summary>
/// Traduce el payload de la Cloud API de Meta — que es el que 360dialog reenvia — a la forma
/// normalizada del dominio. Vive aparte del adaptador para que el proveedor simulado use
/// exactamente el mismo interprete: lo que se prueba en desarrollo es el codigo que corre en
/// produccion.
/// </summary>
public static class InterpreteWebhookMeta
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// <paramref name="ahoraUtc"/> es el instante del reloj de quien llama (Meta, 360dialog o el
    /// simulado), usado solo como respaldo cuando el timestamp del mensaje no viene o no se puede
    /// interpretar (ARQ-01): esta clase es estatica y no tiene un <c>TimeProvider</c> propio que
    /// inyectar.
    /// </summary>
    public static IReadOnlyList<MensajeEntranteDto> Mensajes(
        string cuerpoCrudo, DateTime ahoraUtc, ILogger? log = null)
    {
        var resultado = new List<MensajeEntranteDto>();

        foreach (var valor in LeerValores(cuerpoCrudo, log))
        {
            if (valor.Mensajes is null)
                continue;

            // El nombre de perfil viene en contacts, aparte del mensaje, indexado por wa_id.
            var nombres = valor.Contactos?
                .Where(c => c.WaId is not null)
                .GroupBy(c => c.WaId!)
                .ToDictionary(g => g.Key, g => g.First().Perfil?.Nombre)
                ?? [];

            foreach (var mensaje in valor.Mensajes)
            {
                if (mensaje.Id is null || mensaje.De is null)
                    continue;

                var medio = ExtraerMedio(mensaje);
                var (contenido, idBoton) = medio is null
                    ? ExtraerContenido(mensaje)
                    : (ContenidoDeMedio(medio), null);

                resultado.Add(new MensajeEntranteDto(
                    ProviderMessageId: mensaje.Id,
                    TelefonoE164: NormalizarTelefono(mensaje.De),
                    NombrePerfil: nombres.GetValueOrDefault(mensaje.De),
                    Contenido: contenido,
                    IdBotonPulsado: idBoton,
                    FechaUtc: LeerTimestamp(mensaje.Timestamp, ahoraUtc),
                    Medio: medio));
            }
        }

        return resultado;
    }

    public static IReadOnlyList<EstadoEntregaDto> Estados(
        string cuerpoCrudo, DateTime ahoraUtc, ILogger? log = null)
    {
        var resultado = new List<EstadoEntregaDto>();

        foreach (var valor in LeerValores(cuerpoCrudo, log))
        {
            if (valor.Estados is null)
                continue;

            foreach (var estado in valor.Estados)
            {
                if (estado.Id is null || estado.Estado is null)
                    continue;

                var error = estado.Errores?.FirstOrDefault();

                resultado.Add(new EstadoEntregaDto(
                    ProviderMessageId: estado.Id,
                    Estado: estado.Estado,
                    CodigoError: error?.Codigo?.ToString(),
                    DescripcionError: error is null ? null : $"{error.Titulo} {error.Mensaje}".Trim(),
                    FechaUtc: LeerTimestamp(estado.Timestamp, ahoraUtc)));
            }
        }

        return resultado;
    }

    private static List<WebhookValor> LeerValores(string cuerpoCrudo, ILogger? log)
    {
        try
        {
            var raiz = JsonSerializer.Deserialize<WebhookRaiz>(cuerpoCrudo, Json);

            return raiz?.Entradas?
                .SelectMany(e => e.Cambios ?? [])
                .Select(c => c.Valor)
                .Where(v => v is not null)
                .Select(v => v!)
                .ToList() ?? [];
        }
        catch (JsonException ex)
        {
            // Un payload que no se entiende no debe tumbar el webhook: se registra y se descarta.
            // Reintentarlo daria el mismo resultado y multiplicaria el trabajo.
            log?.LogError(ex, "No se pudo interpretar el cuerpo del webhook.");
            return [];
        }
    }

    /// <summary>
    /// Devuelve el texto a mostrar y, si el postulante uso un boton, su id. Ese id es lo que la
    /// Regla 19 necesita para distinguir una opcion valida del texto libre.
    /// </summary>
    private static (string Contenido, string? IdBoton) ExtraerContenido(WebhookMensaje mensaje) =>
        mensaje.Tipo switch
        {
            "text" => (mensaje.Texto?.Cuerpo ?? string.Empty, null),

            "interactive" when mensaje.Interactivo?.RespuestaBoton is { } b =>
                (b.Titulo ?? string.Empty, b.Id),

            "interactive" when mensaje.Interactivo?.RespuestaLista is { } l =>
                (l.Titulo ?? string.Empty, l.Id),

            // Respuesta rapida de una plantilla: el identificador viaja en payload, no en id.
            "button" => (mensaje.Boton?.Texto ?? string.Empty, mensaje.Boton?.Payload),

            // Ubicaciones, contactos, reacciones y lo que Meta agregue: se deja constancia del tipo
            // para que el analista lo vea en el chat. Los archivos no llegan aca (ExtraerMedio).
            _ => ($"[{mensaje.Tipo ?? "desconocido"}]", null)
        };

    /// <summary>
    /// ARQ-10 (M3): los archivos antes quedaban como <c>[document]</c> y se perdian. Sin id no hay nada
    /// que pedirle al proveedor, y se trata como cualquier otro tipo desconocido.
    /// </summary>
    private static MedioEntranteDto? ExtraerMedio(WebhookMensaje mensaje)
    {
        var medio = mensaje.Tipo switch
        {
            "image" => mensaje.Imagen,
            "document" => mensaje.Documento,
            "audio" => mensaje.Audio,
            "video" => mensaje.Video,
            "sticker" => mensaje.Sticker,
            _ => null
        };

        if (medio?.Id is not { Length: > 0 } id)
            return null;

        return new MedioEntranteDto(
            ProveedorMedioId: id,
            Tipo: mensaje.Tipo!,
            // Meta siempre lo manda; sin el, el almacenamiento decide por el contenido real.
            MimeType: medio.MimeType ?? "application/octet-stream",
            NombreArchivo: string.IsNullOrWhiteSpace(medio.NombreArchivo) ? null : medio.NombreArchivo,
            Leyenda: string.IsNullOrWhiteSpace(medio.Leyenda) ? null : medio.Leyenda);
    }

    /// <summary>
    /// La leyenda es lo que la persona escribio, y es lo que tienen que leer las reglas: el codigo del
    /// aviso junto a la foto del DNI lleva al formulario igual que escrito solo (FUN-02). Sin leyenda,
    /// el tipo en palabras del analista.
    /// </summary>
    private static string ContenidoDeMedio(MedioEntranteDto medio)
    {
        if (medio.Leyenda is { } leyenda)
            return leyenda;

        var tipo = medio.Tipo switch
        {
            "image" => "imagen",
            "document" => "documento",
            _ => medio.Tipo
        };

        return medio.NombreArchivo is { } nombre ? $"[{tipo}: {nombre}]" : $"[{tipo}]";
    }

    private static DateTime LeerTimestamp(string? unixSegundos, DateTime ahoraUtc) =>
        long.TryParse(unixSegundos, out var segundos)
            ? DateTimeOffset.FromUnixTimeSeconds(segundos).UtcDateTime
            : ahoraUtc;

    /// <summary>Meta entrega el numero sin el signo. El dominio lo guarda en E.164.</summary>
    public static string NormalizarTelefono(string numero)
    {
        var limpio = new string([.. numero.Where(char.IsDigit)]);
        return string.IsNullOrEmpty(limpio) ? numero : "+" + limpio;
    }
}
