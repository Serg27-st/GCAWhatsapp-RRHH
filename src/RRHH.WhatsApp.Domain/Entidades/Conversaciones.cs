using RRHH.WhatsApp.Domain.Enums;

namespace RRHH.WhatsApp.Domain.Entidades;

/// <summary>
/// El hilo de WhatsApp. La API entrega los mensajes indexados por numero de telefono, de modo que
/// existe una sola conversacion por numero aunque la persona postule a varias cuentas: la separacion
/// por cuenta de la Regla 6 vive en <see cref="Postulacion"/>, no aca.
/// <para>
/// <see cref="PostulanteId"/> es nulo hasta que se conoce el DNI, porque cuando alguien escribe por
/// primera vez solo tenemos su telefono; el vinculo se establece al completar el JobForms.
/// </para>
/// </summary>
public class Conversacion
{
    public int ConversacionId { get; set; }

    /// <summary>Numero del postulante en formato E.164 (ej. +51987654321). Clave natural del hilo.</summary>
    public required string TelefonoE164 { get; set; }

    public int? PostulanteId { get; set; }

    /// <summary>
    /// Cuenta sobre la que se esta conversando ahora mismo. Nulo mientras el bot no logra
    /// identificarla (<see cref="EstadoConversacion.EnMenuBot"/>) o cuando se rindio
    /// (<see cref="EstadoConversacion.PendienteClasificar"/>, la bandeja general de la Regla 19).
    /// </summary>
    public int? CuentaContextoId { get; set; }

    /// <summary>Analista que atiende el hilo en este momento (el dueno de la cuenta en contexto, o su respaldo tras escalar).</summary>
    public int? AnalistaAtendiendoId { get; set; }

    /// <summary>Nace en el menu del bot (V30): todavia no hay nada que clasificar.</summary>
    public EstadoConversacion Estado { get; set; } = EstadoConversacion.EnMenuBot;

    /// <summary>
    /// R19: intentos del menu sin opcion valida. Reemplaza al conteo derivado del historial (AL2), que
    /// contaba mal. Se reinicia al identificar la cuenta o al volver a entrar al menu.
    /// </summary>
    public int IntentosMenuFallidos { get; set; }

    /// <summary>A12: primer texto libre sin opcion valida en la tanda actual. De aca corre el plazo para derivar.</summary>
    public DateTime? FechaTextoNoReconocido { get; set; }

    /// <summary>P3: cuando entro a «Sin clasificar». De aca corre el plazo del aviso a Jefatura.</summary>
    public DateTime? FechaPendienteDesde { get; set; }

    /// <summary>FUN-06: sello del aviso a Jefatura por un hilo sin clasificar. Un solo aviso por espera.</summary>
    public DateTime? FechaAvisoPendiente { get; set; }

    /// <summary>FUN-05: cuando se escalo al respaldo. De aca corre el segundo nivel (A9).</summary>
    public DateTime? FechaEscalamiento { get; set; }

    /// <summary>FUN-05: sello del aviso de segundo nivel a Jefatura; tambien marca la conversacion como «vencida».</summary>
    public DateTime? FechaAvisoSegundoNivel { get; set; }

    /// <summary>FUN-04, A8: inicio del periodo fuera de horario ya avisado. Un solo aviso por periodo.</summary>
    public DateTime? FechaAvisoFueraHorario { get; set; }

    /// <summary>
    /// Regla 15. Sin una fecha registrada aca, el sistema bloquea todo envio saliente.
    /// El opt-in se guarda por numero de telefono, no por persona, porque es a nivel de numero
    /// que Meta lo evalua.
    /// </summary>
    public DateTime? FechaOptIn { get; set; }

    public OrigenOptIn? OrigenOptIn { get; set; }

    /// <summary>Abre la ventana de 24h de WhatsApp. Solo lo mueve un mensaje ENTRANTE.</summary>
    public DateTime? FechaUltimoMensajeEntrante { get; set; }

    /// <summary>Regla 2: evita recalcular el escalamiento recorriendo toda la tabla de Mensajes.</summary>
    public DateTime? FechaUltimaRespuestaAnalista { get; set; }

    /// <summary>Regla 9: si el postulante vuelve tras 3 dias de inactividad, el bot repregunta la empresa.</summary>
    public DateTime FechaUltimaActividad { get; set; }

    public DateTime FechaCreacion { get; set; }

    /// <summary>Control de concurrencia: el Worker puede escalar justo cuando el analista responde (Seccion 9.6.2).</summary>
    public byte[]? RowVersion { get; set; }

    public Postulante? Postulante { get; set; }
    public Cuenta? CuentaContexto { get; set; }
    public Analista? AnalistaAtendiendo { get; set; }
    public ICollection<Mensaje> Mensajes { get; set; } = [];
}

public class Mensaje
{
    public long MensajeId { get; set; }
    public int ConversacionId { get; set; }

    /// <summary>
    /// Id que asigna el proveedor. Unico: es lo que permite descartar los reintentos de entrega
    /// de Meta sin procesar el mismo mensaje dos veces (idempotencia, Seccion 9.6.2).
    /// </summary>
    public string? ProviderMessageId { get; set; }

    public DireccionMensaje Direccion { get; set; }
    public required string Contenido { get; set; }

    /// <summary>Nulo en texto libre dentro de la ventana de 24h; obligatorio fuera de ella (Regla 15).</summary>
    public int? PlantillaId { get; set; }

    /// <summary>
    /// Parametros con los que se armo la plantilla, serializados. Sin ellos un reintento no podria
    /// reconstruir el mensaje: en <see cref="Contenido"/> queda el texto aprobado con sus {{n}}
    /// sin reemplazar, no el mensaje final.
    /// </summary>
    public string? ParametrosPlantillaJson { get; set; }

    /// <summary>Analista que envio el mensaje. Nulo si lo genero el bot o el Worker.</summary>
    public int? AnalistaId { get; set; }

    public DateTime FechaEnvio { get; set; }
    public EstadoEntrega EstadoEntrega { get; set; } = EstadoEntrega.Pendiente;
    public string? ErrorProveedor { get; set; }

    /// <summary>
    /// Por que fallo el ultimo intento. Es lo que decide si el Worker puede reintentarlo:
    /// solo <see cref="ClaseFallo.Transitorio"/> se reintenta solo.
    /// </summary>
    public ClaseFallo ClaseFallo { get; set; } = ClaseFallo.Ninguno;

    /// <summary>Intentos de envio consumidos, para no reintentar indefinidamente.</summary>
    public int IntentosEnvio { get; set; }

    /// <summary>
    /// Cuando corresponde el proximo intento. Nulo significa que no hay ninguno pendiente, sea
    /// porque el envio salio bien o porque ya no se va a reintentar.
    /// </summary>
    public DateTime? ProximoIntentoUtc { get; set; }

    /// <summary>Permite rastrear un mensaje de punta a punta del flujo cuando algo falla (Seccion 9.6.2).</summary>
    public Guid CorrelationId { get; set; }

    /// <summary>Como se despacha: texto, plantilla, botones o lista (V29).</summary>
    public TipoSaliente TipoSaliente { get; set; } = TipoSaliente.Texto;

    /// <summary>
    /// Opciones de botones o de lista, serializadas, mas el texto del boton de la lista. Sin ellas
    /// el despachador o un reintento no podrian rearmar el menu.
    /// </summary>
    public string? OpcionesJson { get; set; }

    /// <summary>
    /// Clave unica del envio decidido (V29). Reprocesar el mismo evento, o un doble clic del analista,
    /// intenta insertar la misma clave y el indice unico lo impide: el mensaje no se duplica.
    /// Nula en los entrantes, que ya se desduplican por <see cref="ProviderMessageId"/>.
    /// </summary>
    public string? ClaveIdempotencia { get; set; }

    /// <summary>
    /// Cuando se tomo para enviar. Es contra lo que se mide un <c>Enviando</c> atascado: con
    /// <see cref="FechaEnvio"/> —la hora en que se encolo— un mensaje que espero en la cola mientras
    /// el Worker estaba detenido pareceria vencido apenas tomado, y se marcaria ambiguo en pleno envio.
    /// </summary>
    public DateTime? FechaTomaEnvio { get; set; }

    public Conversacion? Conversacion { get; set; }
    public Plantilla? Plantilla { get; set; }
    public Analista? Analista { get; set; }

    /// <summary>Archivos que llegaron con el mensaje (V33). Hoy WhatsApp manda uno por mensaje.</summary>
    public ICollection<MensajeAdjunto> Adjuntos { get; set; } = [];
}

/// <summary>
/// Archivo que mando el postulante por WhatsApp: el CV, la foto del DNI, una nota de voz (V33, ARQ-10).
/// <para>
/// Antes quedaba el texto <c>[document]</c> y nada mas, y el id de medio de Meta caduca: lo que no se
/// registra al recibirlo no se puede recuperar despues. El webhook solo registra; el Worker lo baja,
/// lo escanea y lo guarda con el mismo circuito que el CV del formulario.
/// </para>
/// </summary>
public class MensajeAdjunto
{
    public long AdjuntoId { get; set; }
    public long MensajeId { get; set; }

    /// <summary><c>image</c>, <c>document</c>, <c>audio</c>, <c>video</c> o <c>sticker</c>, como lo nombra Meta.</summary>
    public required string TipoMedio { get; set; }

    /// <summary>Id del medio en el proveedor. Es lo unico con lo que se puede pedir el archivo.</summary>
    public required string ProveedorMedioId { get; set; }

    public required string MimeType { get; set; }

    /// <summary>El nombre con el que lo mando la persona. Solo los documentos lo traen.</summary>
    public string? NombreArchivo { get; set; }

    /// <summary>Nulo hasta que se descarga: el webhook no informa el tamaño.</summary>
    public long? TamanoBytes { get; set; }

    /// <summary>Donde quedo guardado. Nulo mientras no se descarga, si se rechazo y despues de purgarlo.</summary>
    public string? Ruta { get; set; }

    public EstadoAdjunto Estado { get; set; } = EstadoAdjunto.Pendiente;

    /// <summary>Por que no se pudo descargar o por que se rechazo.</summary>
    public string? Error { get; set; }

    /// <summary>
    /// Descargas intentadas. Un fallo transitorio del proveedor se reintenta, pero no para siempre:
    /// el id caduca y reintentarlo despues solo gasta llamadas.
    /// </summary>
    public int IntentosDescarga { get; set; }

    /// <summary>Cuando corresponde el proximo intento. Nulo si puede intentarse ya o si no se va a intentar mas.</summary>
    public DateTime? ProximoIntentoUtc { get; set; }

    public DateTime FechaRecepcion { get; set; }

    public Mensaje? Mensaje { get; set; }
}

/// <summary>
/// Regla 8: derivacion manual a otro analista, de uno en uno. El estado distingue una transferencia
/// enviada de una efectivamente tomada por el analista destino.
/// </summary>
public class Transferencia
{
    public int TransferenciaId { get; set; }
    public int ConversacionId { get; set; }

    /// <summary>Postulacion que se transfiere. Nulo si la conversacion aun no tiene una asociada.</summary>
    public int? PostulacionId { get; set; }

    public int AnalistaOrigenId { get; set; }
    public int AnalistaDestinoId { get; set; }

    /// <summary>Una transferencia urgente se aplica sin esperar la aceptacion del destino.</summary>
    public bool Urgente { get; set; }

    public EstadoTransferencia Estado { get; set; } = EstadoTransferencia.Pendiente;
    public string? Comentario { get; set; }
    public DateTime Fecha { get; set; }
    public DateTime? FechaRespuesta { get; set; }

    /// <summary>
    /// A1: cuando vence la no urgente si el destino no responde (horas habiles). Antes no vencia nunca y
    /// bloqueaba cualquier otra transferencia de la conversacion para siempre (V21). Nula en las urgentes.
    /// </summary>
    public DateTime? FechaVencimiento { get; set; }

    public byte[]? RowVersion { get; set; }

    public Conversacion? Conversacion { get; set; }
    public Postulacion? Postulacion { get; set; }
    public Analista? AnalistaOrigen { get; set; }
    public Analista? AnalistaDestino { get; set; }
}

/// <summary>
/// Catalogo de plantillas aprobadas por Meta. Sin este catalogo no hay forma de enviar el mensaje
/// de fuera de horario (Regla 3), los recordatorios (Regla 9), el cierre automatizado (Regla 12)
/// ni de respetar la ventana de 24h (Regla 15).
/// </summary>
public class Plantilla
{
    public int PlantillaId { get; set; }

    /// <summary>Clave interna con la que el codigo pide la plantilla (ej. "fuera_horario", "recordatorio_24h").</summary>
    public required string Clave { get; set; }

    /// <summary>Nombre exacto con el que la plantilla quedo registrada y aprobada en Meta.</summary>
    public required string NombreMeta { get; set; }

    public CategoriaPlantilla Categoria { get; set; } = CategoriaPlantilla.Utilidad;
    public string Idioma { get; set; } = "es";

    /// <summary>Texto aprobado, con los marcadores {{1}}, {{2}} que Meta usa para los parametros.</summary>
    public required string TextoAprobado { get; set; }

    public int CantidadParametros { get; set; }

    /// <summary>
    /// Nace en false (V8, COR-16): solo se activa a mano cuando Meta aprobó la plantilla. Con true
    /// por defecto, un alta desde código que omitiera el campo quedaba lista para enviar sin
    /// aprobación, que es lo que provoca los bloqueos.
    /// </summary>
    public bool Activa { get; set; } = false;
}
