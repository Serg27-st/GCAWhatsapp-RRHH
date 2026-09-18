using System.Text.Json.Serialization;

namespace RRHH.WhatsApp.Infrastructure.Proveedores;

// Forma del webhook de la Cloud API de Meta, que 360dialog reenvia tal cual. Estos tipos son
// internos a proposito: el resto del sistema solo conoce MensajeEntranteDto y EstadoEntregaDto,
// de modo que cambiar de proveedor no obligue a tocar nada mas (patron Adapter).
//
// Solo se mapea lo que el flujo usa. Los campos que Meta agregue de mas se ignoran, que es
// justamente lo que se espera de un consumidor de webhooks: tolerar lo desconocido.

internal sealed record WebhookRaiz
{
    [JsonPropertyName("object")] public string? Objeto { get; init; }
    [JsonPropertyName("entry")] public List<WebhookEntrada>? Entradas { get; init; }
}

internal sealed record WebhookEntrada
{
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("changes")] public List<WebhookCambio>? Cambios { get; init; }
}

internal sealed record WebhookCambio
{
    [JsonPropertyName("field")] public string? Campo { get; init; }
    [JsonPropertyName("value")] public WebhookValor? Valor { get; init; }
}

internal sealed record WebhookValor
{
    [JsonPropertyName("contacts")] public List<WebhookContacto>? Contactos { get; init; }
    [JsonPropertyName("messages")] public List<WebhookMensaje>? Mensajes { get; init; }
    [JsonPropertyName("statuses")] public List<WebhookEstado>? Estados { get; init; }
}

internal sealed record WebhookContacto
{
    [JsonPropertyName("wa_id")] public string? WaId { get; init; }
    [JsonPropertyName("profile")] public WebhookPerfil? Perfil { get; init; }
}

internal sealed record WebhookPerfil
{
    [JsonPropertyName("name")] public string? Nombre { get; init; }
}

internal sealed record WebhookMensaje
{
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("from")] public string? De { get; init; }

    /// <summary>Unix en segundos, serializado como cadena.</summary>
    [JsonPropertyName("timestamp")] public string? Timestamp { get; init; }

    /// <summary>text, interactive, button, image, document, audio, video, location...</summary>
    [JsonPropertyName("type")] public string? Tipo { get; init; }

    [JsonPropertyName("text")] public WebhookTexto? Texto { get; init; }
    [JsonPropertyName("interactive")] public WebhookInteractivo? Interactivo { get; init; }
    [JsonPropertyName("button")] public WebhookBotonPlantilla? Boton { get; init; }

    // V33: cada tipo de archivo llega bajo su propia clave, con la misma forma.
    [JsonPropertyName("image")] public WebhookMedio? Imagen { get; init; }
    [JsonPropertyName("document")] public WebhookMedio? Documento { get; init; }
    [JsonPropertyName("audio")] public WebhookMedio? Audio { get; init; }
    [JsonPropertyName("video")] public WebhookMedio? Video { get; init; }
    [JsonPropertyName("sticker")] public WebhookMedio? Sticker { get; init; }
}

/// <summary>
/// Un archivo entrante. El archivo no viene: <c>id</c> es lo que se usa para pedirlo, y caduca.
/// <c>filename</c> solo lo traen los documentos; <c>caption</c>, las imagenes, videos y documentos.
/// </summary>
internal sealed record WebhookMedio
{
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("mime_type")] public string? MimeType { get; init; }
    [JsonPropertyName("filename")] public string? NombreArchivo { get; init; }
    [JsonPropertyName("caption")] public string? Leyenda { get; init; }
}

internal sealed record WebhookTexto
{
    [JsonPropertyName("body")] public string? Cuerpo { get; init; }
}

/// <summary>Respuesta a los botones o listas que arma el bot (Regla 19: botones, no texto libre).</summary>
internal sealed record WebhookInteractivo
{
    [JsonPropertyName("type")] public string? Tipo { get; init; }
    [JsonPropertyName("button_reply")] public WebhookOpcion? RespuestaBoton { get; init; }
    [JsonPropertyName("list_reply")] public WebhookOpcion? RespuestaLista { get; init; }
}

internal sealed record WebhookOpcion
{
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("title")] public string? Titulo { get; init; }
}

/// <summary>Respuesta rapida de una plantilla. Trae payload en vez de id.</summary>
internal sealed record WebhookBotonPlantilla
{
    [JsonPropertyName("payload")] public string? Payload { get; init; }
    [JsonPropertyName("text")] public string? Texto { get; init; }
}

internal sealed record WebhookEstado
{
    [JsonPropertyName("id")] public string? Id { get; init; }

    /// <summary>sent, delivered, read, failed.</summary>
    [JsonPropertyName("status")] public string? Estado { get; init; }

    [JsonPropertyName("timestamp")] public string? Timestamp { get; init; }
    [JsonPropertyName("errors")] public List<WebhookError>? Errores { get; init; }
}

internal sealed record WebhookError
{
    [JsonPropertyName("code")] public int? Codigo { get; init; }
    [JsonPropertyName("title")] public string? Titulo { get; init; }
    [JsonPropertyName("message")] public string? Mensaje { get; init; }
}
