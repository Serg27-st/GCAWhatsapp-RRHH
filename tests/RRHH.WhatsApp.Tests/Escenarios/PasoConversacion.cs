namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// Un paso de una conversación de punta a punta (ARQ-12). Los escenarios E## de
/// <c>docs/auditoria/03-analisis-brechas.md</c> §4 se escriben como una lista de pasos, en el orden
/// en que pasan en producción, para que se lean como el guion de la conversación que prueban.
/// </summary>
public abstract record PasoConversacion;

/// <summary>El postulante escribe texto libre. Entra por el webhook con el timestamp del reloj simulado.</summary>
public sealed record Entrante(string Texto, string? Telefono = null) : PasoConversacion;

/// <summary>
/// El postulante manda un archivo (ARQ-10): <c>image</c>, <c>document</c>, <c>audio</c>, <c>video</c> o
/// <c>sticker</c>, con la forma de la Cloud API. <paramref name="Leyenda"/> es lo que escribió junto al
/// archivo, y es lo que leen las reglas.
/// </summary>
public sealed record Medio(
    string Tipo = "document",
    string MimeType = "application/pdf",
    string? NombreArchivo = "cv.pdf",
    string? Leyenda = null,
    string? Telefono = null) : PasoConversacion;

/// <summary>El postulante pulsa un botón o una fila de lista del bot (el id es el de <c>IdsBoton</c>).</summary>
public sealed record Boton(string IdBoton, string Titulo = "opcion", string? Telefono = null) : PasoConversacion;

/// <summary>Pasa el tiempo en todo el circuito.</summary>
public sealed record Avanzar(TimeSpan Lapso) : PasoConversacion;

/// <summary>
/// El postulante completa el formulario de la última invitación de su conversación, como lo manda
/// el Apps Script (V27).
/// </summary>
public sealed record Formulario(
    string Dni = "45678912",
    string NombreCompleto = "Maria Quispe",
    bool Consentimiento = true,
    string? Telefono = null) : PasoConversacion;

/// <summary>
/// El analista responde desde la bandeja. Sin <paramref name="AnalistaId"/> responde quien atiende
/// la conversación.
/// </summary>
public sealed record RespuestaAnalista(
    string Texto,
    int? AnalistaId = null,
    string? ClavePlantilla = null,
    string? Telefono = null) : PasoConversacion;

/// <summary>Una vuelta del barrido por tiempo del Worker: todas las conversaciones y todas las postulaciones.</summary>
public sealed record Barrido : PasoConversacion;

/// <summary>
/// Meta acusa el último mensaje que salió hacia ese teléfono: <c>sent</c>, <c>delivered</c>,
/// <c>read</c> o <c>failed</c>. Entra por el mismo webhook que los mensajes, con la forma de la
/// Cloud API; <paramref name="CodigoError"/> y <paramref name="TituloError"/> solo van con <c>failed</c>.
/// </summary>
public sealed record Acuse(
    string Estado,
    int? CodigoError = null,
    string? TituloError = null,
    string? Telefono = null) : PasoConversacion;

/// <summary>Una vuelta del consumidor de la outbox del Worker.</summary>
public sealed record ConsumirOutbox : PasoConversacion;

/// <summary>
/// Una vuelta de la descarga de adjuntos del Worker (V33): baja del proveedor simulado lo que el
/// webhook registró, lo escanea y lo guarda.
/// </summary>
public sealed record DescargarAdjuntos : PasoConversacion;

/// <summary>
/// Una vuelta del despachador de envíos (V29): lo que el bot encoló sale hacia el proveedor simulado.
/// Sin este paso, lo decidido queda <c>EnCola</c> y el escenario no ve ningún envío.
/// </summary>
public sealed record Despachar : PasoConversacion;
