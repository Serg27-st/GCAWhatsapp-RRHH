namespace RRHH.WhatsApp.Domain.Entidades;

/// <summary>
/// Registro del link de JobForms enviado a un postulante y su seguimiento. Es lo que el Worker
/// consulta para disparar el recordatorio de 24h y el aviso al analista de 48h (Regla 9): sin
/// esta tabla no hay forma de saber quien recibio el link y no lo completo.
/// </summary>
public class JobFormsInvitacion
{
    public int InvitacionId { get; set; }
    public int ConversacionId { get; set; }

    /// <summary>Nulo cuando todavia no se conoce el DNI: el link se envia antes de tener al postulante creado.</summary>
    public int? PostulanteId { get; set; }

    public int HcId { get; set; }

    /// <summary>
    /// Token aleatorio que viaja en la URL en lugar del HcId secuencial, para que nadie pueda
    /// enumerar vacantes de otras cuentas cambiando el numero (Seccion 9.6.1).
    /// </summary>
    public Guid Token { get; set; } = Guid.NewGuid();

    public DateTime FechaEnvioLink { get; set; }

    public bool RecordatorioEnviado { get; set; }
    public DateTime? FechaRecordatorio { get; set; }

    public bool AvisoAnalistaEnviado { get; set; }
    public DateTime? FechaAvisoAnalista { get; set; }

    public bool Completado { get; set; }
    public DateTime? FechaCompletado { get; set; }

    public Conversacion? Conversacion { get; set; }
    public Postulante? Postulante { get; set; }
    public Hc? Hc { get; set; }
}

/// <summary>
/// Respuesta del postulante al formulario. <see cref="VersionAvisoPrivacidad"/> deja trazabilidad
/// legal de que texto exacto acepto (Regla 17); mientras el formulario viva en Google Forms este
/// campo se llena con la version vigente al momento del envio, no con la que el postulante vio.
/// </summary>
public class JobFormsRespuesta
{
    public int RespuestaId { get; set; }
    public int? InvitacionId { get; set; }
    public int PostulanteId { get; set; }
    public int HcId { get; set; }

    /// <summary>Respuestas de los campos opcionales del HC, serializadas como JSON.</summary>
    public required string DatosJson { get; set; }

    /// <summary>Ruta del CV en el almacenamiento externo. Nunca el archivo dentro de SQL Server (Seccion 8.3).</summary>
    public string? CvUrl { get; set; }

    public bool ConsentimientoAceptado { get; set; }
    public string? VersionAvisoPrivacidad { get; set; }
    public DateTime? FechaConsentimiento { get; set; }
    public DateTime FechaEnvio { get; set; }

    public JobFormsInvitacion? Invitacion { get; set; }
    public Postulante? Postulante { get; set; }
    public Hc? Hc { get; set; }
}
