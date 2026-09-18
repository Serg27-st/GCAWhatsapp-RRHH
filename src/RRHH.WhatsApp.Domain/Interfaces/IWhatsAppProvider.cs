using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;

namespace RRHH.WhatsApp.Domain.Interfaces;

/// <summary>Mensaje entrante ya normalizado, independiente del proveedor que lo entrego.</summary>
/// <param name="Medio">El archivo que trae, si trae uno (ARQ-10). Nulo en texto, botones y ubicaciones.</param>
public sealed record MensajeEntranteDto(
    string ProviderMessageId,
    string TelefonoE164,
    string? NombrePerfil,
    string Contenido,
    string? IdBotonPulsado,
    DateTime FechaUtc,
    MedioEntranteDto? Medio = null);

/// <summary>
/// Lo que el webhook dice de un archivo entrante (V33): lo necesario para pedirlo al proveedor y para
/// mostrarlo. El archivo en si no viene: se descarga aparte, y el id caduca.
/// </summary>
/// <param name="Tipo"><c>image</c>, <c>document</c>, <c>audio</c>, <c>video</c> o <c>sticker</c>.</param>
/// <param name="Leyenda">El texto que la persona escribio junto al archivo.</param>
public sealed record MedioEntranteDto(
    string ProveedorMedioId,
    string Tipo,
    string MimeType,
    string? NombreArchivo,
    string? Leyenda);

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

    /// <summary>
    /// Baja un archivo que mando el postulante (V33). El adaptador sigue siendo lo unico que habla
    /// con el proveedor, tambien para esto.
    /// <para>
    /// No pasa por el limitador de envio: no es un mensaje saliente y no forma parte del patron que
    /// provoco el bloqueo. Nunca lanza por un fallo del proveedor: lo clasifica.
    /// </para>
    /// </summary>
    Task<ResultadoDescarga> DescargarMedioAsync(string proveedorMedioId, CancellationToken ct = default);
}

/// <summary>
/// Un archivo bajado del proveedor. Quien lo recibe es dueño del flujo y tiene que cerrarlo: detras
/// hay una conexion abierta, porque un documento puede pesar decenas de megas y no se carga en memoria.
/// </summary>
public sealed record MedioDescargado(Stream Contenido, string MimeType, long? Tamano) : IDisposable, IAsyncDisposable
{
    public void Dispose() => Contenido.Dispose();

    public ValueTask DisposeAsync() => Contenido.DisposeAsync();
}

/// <summary>
/// En que quedo pedir un archivo (V33).
/// <para>
/// A diferencia de <see cref="ResultadoEnvio"/>, no hay fallo ambiguo: pedir un archivo no cambia nada
/// del lado del proveedor, y repetirlo no duplica nada. Lo que importa es si vale la pena reintentar
/// antes de que el id caduque.
/// </para>
/// </summary>
public sealed record ResultadoDescarga(MedioDescargado? Medio, string? Error, ClaseFallo Clase = ClaseFallo.Ninguno)
{
    public bool Exito => Medio is not null;

    public static ResultadoDescarga Ok(MedioDescargado medio) => new(medio, null);

    /// <summary>El proveedor no respondio, se corto la conexion o esta saturado: se puede volver a pedir.</summary>
    public static ResultadoDescarga Transitorio(string error) => new(null, error, ClaseFallo.Transitorio);

    /// <summary>El id vencio o no existe, o faltan credenciales: pedirlo de nuevo da lo mismo.</summary>
    public static ResultadoDescarga Permanente(string error) => new(null, error, ClaseFallo.Permanente);
}
