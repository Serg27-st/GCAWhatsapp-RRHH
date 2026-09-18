using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;

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
    CambioEstadoPostulacion = 5,

    /// <summary>
    /// Tick del Worker sobre una postulacion: cierre de cortesia pendiente, aviso y archivado (FUN-10,
    /// FUN-11). Va aparte de <see cref="TiempoTranscurrido"/> porque el contexto tambien trae la
    /// conversacion: con el mismo disparador, las reglas por tiempo del hilo —escalar, derivar,
    /// vencer transferencias— correrian una vez mas por cada postulacion de la persona.
    /// </summary>
    TiempoTranscurridoPostulacion = 6
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

    /// <summary>
    /// Base de la clave de idempotencia de los envios que decida esta evaluacion (V29). El ejecutor
    /// le agrega el indice de cada accion: reevaluar el mismo disparador —el mismo evento de la
    /// outbox— produce las mismas claves, y el indice unico impide encolar dos veces el mismo mensaje.
    /// </summary>
    public string ClaveEjecucion { get; init; } = Guid.NewGuid().ToString("N");

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

    /// <summary>
    /// Vacantes abiertas de la cuenta en contexto. Regla 20: si esta vacia, no hay a que postular
    /// y el bot tiene que decirlo en vez de mandar un enlace muerto.
    /// </summary>
    public IReadOnlyList<Hc> VacantesAbiertas { get; init; } = [];

    /// <summary>
    /// Vacantes de este hilo a las que ya se les mando el enlace del JobForms. Evita reenviarlo en
    /// cada mensaje del postulante, y tambien reenviarlo despues de que ya completo el formulario.
    /// </summary>
    public IReadOnlyList<int> HcsConInvitacion { get; init; } = [];

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
    /// Las postulaciones del mismo postulante en todas las cuentas, con la cuenta y la vacante ya
    /// resueltas (R9, R16, A2, A3).
    /// <para>
    /// Reemplazo a <c>EstadosPostulaciones</c>: con solo el estado no se sabia <em>donde</em>
    /// estaba vivo el proceso, y la desambiguacion entre cuentas (A3) necesita ofrecerle al postulante
    /// sus procesos por nombre.
    /// </para>
    /// </summary>
    public IReadOnlyList<PostulacionVigente> PostulacionesDelPostulante { get; init; } = [];

    /// <summary>
    /// C1, A13: <c>FechaUltimaActividad</c> de la conversacion tal como estaba <em>antes</em> de registrar
    /// el mensaje que disparo esta evaluacion. La toma el webhook y viaja en el evento: leerla despues
    /// da siempre «hace un instante», porque ese mismo mensaje ya la movio.
    /// </summary>
    public DateTime? FechaActividadAnterior { get; init; }

    /// <summary>
    /// FUN-04: cuando empezo el periodo fuera de horario actual (el ultimo cierre). La Regla 3 avisa una
    /// vez por periodo y no una vez cada 8 horas. Nulo dentro del horario.
    /// </summary>
    public DateTime? InicioPeriodoFueraHorario { get; init; }

    /// <summary>FUN-04: la proxima apertura del horario de atencion, para decirle al postulante cuando le responden.</summary>
    public DateTime? ProximaApertura { get; init; }

    /// <summary>
    /// FUN-04: el horario vigente escrito para una persona («de lunes a viernes de 09:00 a 18:00»). Lo
    /// arma el calendario a partir de los tramos configurados; antes era un parametro de configuracion
    /// que nadie actualizaba al cambiar el horario real (COR-01).
    /// </summary>
    public string? DescripcionHorario { get; init; }

    /// <summary>
    /// AL2, FUN-02: como se eligio la cuenta o la vacante de este mensaje. La Regla 19 cuenta un intento
    /// fallido solo con <see cref="OrigenEleccion.Ninguna"/>, y la Regla 9 manda el enlace sin menu solo
    /// si la vacante la eligio el postulante (boton o codigo); un contexto que ya estaba no alcanza para
    /// volver a ofrecerle el menu de vacantes.
    /// </summary>
    public OrigenEleccion OrigenEleccion { get; init; } = OrigenEleccion.Ninguna;

    /// <summary>
    /// FUN-03: pagina del menu de empresas que pidio el postulante con el boton <c>pag_{n}</c>. Cero si no
    /// pulso uno: navegar el menu no es un intento fallido y la Regla 19 tiene que poder distinguirlo.
    /// </summary>
    public int PaginaMenu { get; init; }

    /// <summary>FUN-09: postulacion que eligio el postulante en el menu de procesos (boton <c>proc_{id}</c>).</summary>
    public int? PostulacionElegidaId { get; init; }

    /// <summary>
    /// FUN-09: el postulante pulso «Otra empresa» en el menu de procesos. No es una eleccion de cuenta
    /// sino lo contrario: quiere postular a algo nuevo, asi que el contexto que tenia deja de valer.
    /// </summary>
    public bool PidioOtraEmpresa { get; init; }

    /// <summary>FUN-07, A1: transferencia de la conversacion todavia sin respuesta. La Regla 8 la vence al pasar su plazo.</summary>
    public Transferencia? TransferenciaPendiente { get; init; }

    /// <summary>
    /// FUN-10, A11: el analista dejo marcada la casilla de enviar el cierre de cortesia al descartar.
    /// Viaja en el evento <c>PostulacionDescartada</c> porque es lo que eligio en ese dialogo.
    /// </summary>
    public bool EnviarCierreSolicitado { get; init; }

    /// <summary>
    /// FUN-05, A9: minutos habiles desde que la conversacion se escalo al respaldo. Nulo si no esta escalada.
    /// Habiles porque el segundo nivel avisa a Jefatura, que tampoco trabaja de madrugada.
    /// </summary>
    public double? MinutosHabilesDesdeEscalamiento { get; init; }

    /// <summary>FUN-06, P3: minutos habiles en «Sin clasificar». Nulo si la conversacion no esta ahi.</summary>
    public double? MinutosHabilesEnPendiente { get; init; }

    /// <summary>
    /// FUN-06, A12: minutos habiles desde el ultimo texto que el bot no reconocio. Nulo si no hay uno
    /// pendiente. Con silencio posterior, la conversacion se deriva en vez de quedar esperando el menu.
    /// </summary>
    public double? MinutosHabilesDesdeTextoNoReconocido { get; init; }

    /// <summary>
    /// Regla 9: enlace del JobForms que corresponde a <see cref="Invitacion"/>, ya armado con su
    /// token. La regla lo necesita como parametro de la plantilla del recordatorio, pero armar la
    /// URL no es decision suya: se resuelve al construir el contexto.
    /// </summary>
    public string? EnlaceInvitacion { get; init; }

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

    /// <summary>
    /// A2: el postulante tiene al menos un proceso en curso o de reingreso. Un reingreso cuenta igual
    /// que uno en curso: no se archiva (R16) ni se repregunta la empresa (R9).
    /// </summary>
    public bool TieneProcesoVivo => PostulacionesDelPostulante.Any(p => EsVivo(p.Estado));

    /// <summary>
    /// A3: cuentas con un proceso vivo, sin repetir y en el orden en que llegaron. Dos vacantes vivas en
    /// la misma cuenta son una sola opcion: lo que hay que desambiguar es a quien le escribe, no a que vacante.
    /// </summary>
    public IReadOnlyList<int> CuentasVivas =>
        PostulacionesDelPostulante.Where(p => EsVivo(p.Estado)).Select(p => p.CuentaId).Distinct().ToList();

    /// <summary>R9, C1: dias de inactividad antes de este mensaje, medidos contra la instantanea del webhook.</summary>
    public double? DiasDesdeActividadAnterior => (AhoraUtc - FechaActividadAnterior)?.TotalDays;

    private static bool EsVivo(EstadoPostulacion estado) =>
        estado is EstadoPostulacion.EnProceso or EstadoPostulacion.Reingreso;
}

/// <summary>
/// Una postulacion del postulante con su cuenta y vacante ya resueltas (ARQ-07). Lleva los nombres
/// para que la regla pueda armar el menu de procesos sin volver a la base.
/// </summary>
public sealed record PostulacionVigente(
    int PostulacionId,
    int CuentaId,
    string Cuenta,
    int HcId,
    string Vacante,
    EstadoPostulacion Estado,
    int? AnalistaAsignadoId,
    DateTime FechaUltimaActividad);

/// <summary>
/// Como se eligio la cuenta o la vacante del mensaje (AL2, FUN-02). Distingue lo que el postulante
/// eligio en este mensaje de lo que ya venia fijado en la conversacion.
/// </summary>
public enum OrigenEleccion
{
    /// <summary>Todavia no hay cuenta elegida.</summary>
    Ninguna = 0,

    /// <summary>El postulante pulso el boton de una cuenta o de una vacante.</summary>
    Boton = 1,

    /// <summary>El postulante escribio el codigo de aviso de una vacante (FUN-02).</summary>
    CodigoAviso = 2,

    /// <summary>El postulante escribio el nombre de la cuenta.</summary>
    NombreCuenta = 3,

    /// <summary>La cuenta ya estaba fijada en la conversacion por una eleccion anterior.</summary>
    Contexto = 4
}
