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
    /// identificarla, lo que junto con <see cref="EstadoConversacion.PendienteClasificar"/>
    /// sostiene la bandeja general de la Regla 19.
    /// </summary>
    public int? CuentaContextoId { get; set; }

    /// <summary>Analista que atiende el hilo en este momento (el dueno de la cuenta en contexto, o su respaldo tras escalar).</summary>
    public int? AnalistaAtendiendoId { get; set; }

    public EstadoConversacion Estado { get; set; } = EstadoConversacion.PendienteClasificar;

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

    public Conversacion? Conversacion { get; set; }
    public Plantilla? Plantilla { get; set; }
    public Analista? Analista { get; set; }
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
    public bool Activa { get; set; } = true;
}
