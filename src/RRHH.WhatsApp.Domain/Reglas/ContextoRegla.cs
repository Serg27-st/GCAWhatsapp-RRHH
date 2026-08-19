using RRHH.WhatsApp.Domain.Entidades;

namespace RRHH.WhatsApp.Domain.Reglas;

/// <summary>Que provoco la evaluacion de las reglas.</summary>
public enum TipoDisparador
{
    /// <summary>Llego un mensaje del postulante por el webhook.</summary>
    MensajeEntrante = 1,

    /// <summary>Un analista o el sistema quiere enviar un mensaje. Aca se aplica el bloqueo de la Regla 15.</summary>
    EnvioSaliente = 2,

    /// <summary>El postulante completo el JobForms.</summary>
    JobFormsCompletado = 3,

    /// <summary>Tick del Worker: escalamientos, recordatorios, archivado, ausencias.</summary>
    TiempoTranscurrido = 4,

    /// <summary>El analista movio una postulacion de etapa o la marco whitelist/blacklist.</summary>
    CambioEstadoPostulacion = 5
}

/// <summary>
/// Estado que el motor carga una sola vez y pasa a todas las reglas. Las reglas leen de aca y
/// no consultan la base por su cuenta: asi cada una queda testeable de forma aislada (Seccion 9.6.6).
/// </summary>
public sealed class ContextoRegla
{
    public required TipoDisparador Disparador { get; init; }
    public required DateTime AhoraUtc { get; init; }
    public Guid CorrelationId { get; init; } = Guid.NewGuid();

    public Conversacion? Conversacion { get; init; }
    public Mensaje? MensajeEntrante { get; init; }
    public Postulante? Postulante { get; init; }
    public Postulacion? Postulacion { get; init; }
    public Cuenta? Cuenta { get; init; }
    public Hc? Hc { get; init; }

    /// <summary>Analista dueno de la cuenta en contexto (Regla 1).</summary>
    public Analista? AnalistaTitular { get; init; }

    /// <summary>Respaldo fijo de la cuenta, destino del escalamiento (Regla 2) y de las ausencias (Regla 14).</summary>
    public Analista? AnalistaRespaldo { get; init; }

    /// <summary>Otras cuentas en las que el mismo DNI esta en proceso, para el aviso generico de la Regla 6.</summary>
    public IReadOnlyList<Cuenta> OtrasCuentasEnProceso { get; init; } = [];

    /// <summary>Regla 14: el titular esta de vacaciones o descanso medico.</summary>
    public bool TitularAusente { get; init; }

    /// <summary>Regla 3: el mensaje llego dentro del horario de atencion configurado.</summary>
    public bool DentroDeHorario { get; init; }

    /// <summary>Invitacion de JobForms pendiente, si la hay (Regla 9).</summary>
    public JobFormsInvitacion? Invitacion { get; init; }

    /// <summary>Parametros de <see cref="ClavesConfiguracion"/> ya resueltos, para no leerlos regla por regla.</summary>
    public required IReadOnlyDictionary<string, string> Configuracion { get; init; }

    /// <summary>Cuantos intentos lleva el bot mostrando el menu sin recibir una opcion valida (Regla 19).</summary>
    public int IntentosMenuFallidos { get; init; }

    /// <summary>
    /// Minutos a reloj corrido desde que el postulante escribio sin obtener respuesta del analista.
    /// Nulo si no hay nada pendiente de responder.
    /// </summary>
    public double? MinutosSinRespuestaReloj { get; init; }

    /// <summary>
    /// Los mismos minutos, pero contando solo el tiempo dentro del horario de atencion. La Regla 2
    /// elige cual de los dos usar segun <see cref="ClavesConfiguracion.EscalamientoSoloHorarioLaboral"/>:
    /// el calendario laboral lo resuelve IHorarioAtencionService al armar el contexto, y la decision
    /// de que hacer con el se queda en la regla.
    /// </summary>
    public double? MinutosSinRespuestaHabiles { get; init; }

    public int ConfigInt(string clave, int porDefecto) =>
        Configuracion.TryGetValue(clave, out var v) && int.TryParse(v, out var n) ? n : porDefecto;

    public bool ConfigBool(string clave, bool porDefecto) =>
        Configuracion.TryGetValue(clave, out var v) && bool.TryParse(v, out var b) ? b : porDefecto;

    /// <summary>
    /// Regla 15: la ventana de servicio de 24h sigue abierta, medida desde el ultimo mensaje
    /// ENTRANTE. Fuera de ella todo envio debe ir como plantilla aprobada por Meta.
    /// </summary>
    public bool VentanaServicioAbierta =>
        Conversacion?.FechaUltimoMensajeEntrante is { } ultimo && (AhoraUtc - ultimo) < TimeSpan.FromHours(24);

    /// <summary>Regla 15: existe consentimiento registrado para escribirle a este numero.</summary>
    public bool TieneOptIn => Conversacion?.FechaOptIn is not null;
}
