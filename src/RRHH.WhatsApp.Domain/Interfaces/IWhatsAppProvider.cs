using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;

namespace RRHH.WhatsApp.Domain.Interfaces;

/// <summary>Mensaje entrante ya normalizado, independiente del proveedor que lo entrego.</summary>
public sealed record MensajeEntranteDto(
    string ProviderMessageId,
    string TelefonoE164,
    string? NombrePerfil,
    string Contenido,
    string? IdBotonPulsado,
    DateTime FechaUtc);

/// <summary>
/// Resultado de un envio. <paramref name="Clase"/> es lo que permite decidir si reintentar:
/// sin ella, cualquier reintento seria a ciegas y podria duplicar un mensaje que ya salio.
/// </summary>
public sealed record ResultadoEnvio(
    bool Exito,
    string? ProviderMessageId,
    string? Error,
    ClaseFallo Clase = ClaseFallo.Ninguno)
{
    public static ResultadoEnvio Ok(string? providerMessageId) => new(true, providerMessageId, null);

    /// <summary>Rechazo sin procesar: el mensaje no salio y reintentarlo es seguro.</summary>
    public static ResultadoEnvio Transitorio(string error) => new(false, null, error, ClaseFallo.Transitorio);

    /// <summary>Reintentarlo daria exactamente el mismo resultado.</summary>
    public static ResultadoEnvio Permanente(string error) => new(false, null, error, ClaseFallo.Permanente);

    /// <summary>Se perdio la respuesta: puede haber salido o no.</summary>
    public static ResultadoEnvio Ambiguo(string error) => new(false, null, error, ClaseFallo.Ambiguo);
}

/// <summary>Acuse de entrega de un mensaje que ya salio, para actualizar Mensajes.EstadoEntrega.</summary>
public sealed record EstadoEntregaDto(
    string ProviderMessageId,
    string Estado,
    string? CodigoError,
    string? DescripcionError,
    DateTime FechaUtc);

/// <summary>Boton del menu de empresas que arma el bot (Regla 19: botones, no texto libre).</summary>
public sealed record BotonRespuesta(string Id, string Titulo);

/// <summary>
/// Patron Adapter: interfaz unica hacia el proveedor de WhatsApp. Cambiar de 360dialog a Meta
/// Cloud API implica reemplazar una implementacion, no reescribir el sistema.
/// <para>
/// La implementacion es tambien la responsable de limitar la velocidad de envio saliente
/// (Seccion 9.6.4), para no repetir el patron de uso que causo el bloqueo original.
/// </para>
/// </summary>
public interface IWhatsAppProvider
{
    string Nombre { get; }

    Task<ResultadoEnvio> EnviarTextoAsync(string telefonoE164, string texto, CancellationToken ct = default);

    Task<ResultadoEnvio> EnviarPlantillaAsync(
        string telefonoE164,
        Plantilla plantilla,
        IReadOnlyList<string> parametros,
        CancellationToken ct = default);

    /// <summary>Hasta 3 opciones. Con mas, WhatsApp exige una lista.</summary>
    Task<ResultadoEnvio> EnviarBotonesAsync(
        string telefonoE164,
        string texto,
        IReadOnlyList<BotonRespuesta> botones,
        CancellationToken ct = default);

    /// <summary>
    /// Menu desplegable. WhatsApp admite hasta 10 filas en total, lo que fija el techo del menu
    /// de empresas de la Regla 19.
    /// </summary>
    Task<ResultadoEnvio> EnviarListaAsync(
        string telefonoE164,
        string texto,
        string textoBoton,
        IReadOnlyList<BotonRespuesta> opciones,
        CancellationToken ct = default);

    /// <summary>Valida la firma de la peticion antes de procesar nada. El Gateway no confia en el cuerpo sin esto.</summary>
    bool ValidarFirma(string cuerpoCrudo, IReadOnlyDictionary<string, string> cabeceras);

    /// <summary>Traduce el payload propio del proveedor a la forma normalizada del dominio.</summary>
    IReadOnlyList<MensajeEntranteDto> InterpretarWebhook(string cuerpoCrudo);

    /// <summary>
    /// Acuses de entrega presentes en el mismo payload. Van aparte de los mensajes entrantes
    /// porque no abren la ventana de 24h ni cuentan como opt-in: solo actualizan el estado de
    /// un mensaje que ya salio.
    /// </summary>
    IReadOnlyList<EstadoEntregaDto> InterpretarEstados(string cuerpoCrudo);
}
