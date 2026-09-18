using RRHH.WhatsApp.Domain.Enums;

namespace RRHH.WhatsApp.Domain.Reglas;

/// <summary>
/// Lo que una regla decide que debe pasar. Las reglas no envian mensajes ni escriben en las tablas
/// de otros modulos: solo devuelven acciones, y la capa de aplicacion las ejecuta. Es lo que hace
/// que cada regla se pueda probar sin base de datos ni proveedor de WhatsApp.
/// <para>
/// Las acciones que sellan algo llevan <c>SoloSiSeEnvioAnterior</c> (COR-03): el ejecutor las saltea
/// si el envio inmediatamente anterior no llego a encolarse. Sellar un recordatorio que no salio lo
/// pierde para siempre, porque el barrido lo da por hecho y nadie lo vuelve a intentar.
/// </para>
/// </summary>
public abstract record AccionRegla;

/// <summary>Envia una plantilla aprobada por Meta, resuelta por su clave interna.</summary>
public sealed record EnviarPlantilla(string ClavePlantilla, IReadOnlyList<string> Parametros) : AccionRegla;

/// <summary>Envia texto libre. Solo valido con la ventana de servicio abierta (Regla 15).</summary>
public sealed record EnviarTextoLibre(string Texto) : AccionRegla;

/// <summary>
/// COR-03 (P1): un mensaje del bot que tiene que llegar aunque Meta no haya aprobado su plantilla. Con
/// la ventana de 24h abierta sale como <paramref name="Texto"/>; cerrada, como la plantilla activa de
/// <paramref name="ClavePlantilla"/>; sin ninguna de las dos, no sale y queda una alerta operativa.
/// <para>
/// Existe porque las plantillas arrancan inactivas: con <see cref="EnviarPlantilla"/> el bot quedaba
/// mudo justo en las respuestas que el postulante espera dentro de la ventana (C3).
/// </para>
/// </summary>
public sealed record EnviarMensajeBot(string Texto, string? ClavePlantilla, IReadOnlyList<string> Parametros) : AccionRegla;

/// <summary>
/// Muestra el menu de empresas con botones. Regla 19 marca el reintento. <paramref name="Pagina"/>
/// existe porque WhatsApp admite 10 filas y hay mas cuentas que eso (FUN-03).
/// </summary>
public sealed record MostrarMenuEmpresas(bool EsReintento, int Pagina = 1) : AccionRegla
{
    /// <summary>
    /// FUN-11: la persona vuelve despues de un archivado. El menu la saluda como a alguien conocido,
    /// no como a quien escribe por primera vez.
    /// </summary>
    public bool EsRegreso { get; init; }
}

/// <summary>
/// FUN-09 (A3): el postulante tiene procesos vivos en mas de una cuenta y escribio sin decir por cual.
/// Le ofrece sus procesos por nombre y la opcion «Otra empresa», en vez de adivinar.
/// </summary>
public sealed record MostrarMenuProcesos : AccionRegla;

/// <summary>
/// FUN-08, FUN-09: fija la cuenta de la postulacion y su analista asignado (o el titular si no tiene).
/// Es lo que evita que un postulante con un proceso vivo tenga que volver a elegir empresa.
/// </summary>
public sealed record TomarContextoDePostulacion(int PostulacionId) : AccionRegla;

/// <summary>
/// COR-06: suma un intento del menu sin opcion valida. El contador vive en la conversacion porque
/// contarlo desde los mensajes sumaba todo el historial: un postulante que volvia iba directo a «Sin
/// clasificar» sin ver el menu (AL2).
/// </summary>
public sealed record RegistrarIntentoMenu(bool TextoNoReconocido) : AccionRegla;

/// <summary>COR-06: el postulante eligio o lo tomo una persona; el contador del menu vuelve a cero.</summary>
public sealed record ReiniciarIntentosMenu : AccionRegla;

/// <summary>FUN-06 (P3): el bot no pudo clasificar al postulante y lo deja en «Sin clasificar» con plazo.</summary>
public sealed record DerivarAPendientes(string Motivo) : AccionRegla;

public sealed record EnviarLinkJobForms(int HcId) : AccionRegla;

/// <summary>
/// Regla 9 y Regla 20: menu de vacantes abiertas de una cuenta. Aparece cuando la cuenta tiene
/// mas de un HC, porque el enlace del JobForms es por vacante y no por cliente.
/// </summary>
public sealed record MostrarMenuVacantes(int CuentaId) : AccionRegla;

public sealed record AsignarAnalista(int AnalistaId, string Motivo) : AccionRegla;

/// <summary>
/// Regla 2 y Regla 14: pasa la conversacion al respaldo fijo de la cuenta.
/// <para>
/// <paramref name="AnalistaEsperadoId"/> es quien atendia cuando se armo el contexto (COR-13, M4). Entre
/// la lectura y la ejecucion el titular puede haber respondido o transferido: si ya no es el mismo, el
/// servicio no escala.
/// </para>
/// </summary>
public sealed record EscalarARespaldo(int AnalistaEsperadoId, int AnalistaRespaldoId, string Motivo) : AccionRegla;

public sealed record EstablecerCuentaContexto(int CuentaId) : AccionRegla;

public sealed record CambiarEstadoConversacion(EstadoConversacion Estado) : AccionRegla;

public sealed record MoverEtapaKanban(int PostulacionId, int EtapaId) : AccionRegla;

/// <summary>Regla 6: aviso generico, sin detalle de mensajes, de que el postulante esta en otra cuenta.</summary>
public sealed record NotificarAnalista(int AnalistaId, string Mensaje) : AccionRegla;

/// <summary>
/// FUN-05, FUN-06: avisa a todos los analistas activos de un rol. La regla no sabe quienes son hoy los
/// de Jefatura, y no deberia: eso cambia sin que cambie la regla.
/// </summary>
public sealed record NotificarRol(RolAnalista Rol, string Mensaje) : AccionRegla;

/// <summary>
/// FUN-04 a FUN-06: sella en la conversacion que un aviso ya salio, para que el proximo barrido o el
/// proximo mensaje no lo repita (P4).
/// </summary>
public sealed record SellarConversacion(MarcaConversacion Marca) : AccionRegla
{
    /// <summary>COR-03: no sellar si el envio anterior no salio. Ver <see cref="AccionRegla"/>.</summary>
    public bool SoloSiSeEnvioAnterior { get; init; }
}

/// <summary>FUN-11 (A14): la postulacion pasa a <see cref="EstadoPostulacion.Archivada"/>.</summary>
public sealed record ArchivarPostulacion(int PostulacionId, string Motivo) : AccionRegla;

/// <summary>FUN-10, FUN-11: sella un aviso o el cierre de cortesia de una postulacion para que salga una sola vez (P4).</summary>
public sealed record SellarPostulacion(int PostulacionId, MarcaPostulacion Marca) : AccionRegla
{
    /// <summary>COR-03: no sellar si el envio anterior no salio. Ver <see cref="AccionRegla"/>.</summary>
    public bool SoloSiSeEnvioAnterior { get; init; }
}

/// <summary>FUN-07 (A1): la transferencia no urgente vencio sin respuesta; el hilo sigue con quien la envio.</summary>
public sealed record VencerTransferencia(int TransferenciaId) : AccionRegla;

/// <summary>FUN-11: una conversacion archivada vuelve a la vida porque el postulante escribio.</summary>
public sealed record ReactivarConversacion(EstadoConversacion Nuevo) : AccionRegla;

public sealed record RegistrarOptIn(OrigenOptIn Origen) : AccionRegla;

/// <summary>
/// Regla 15: corta el envio saliente. Es la unica accion que detiene el pipeline;
/// las demas se acumulan y se ejecutan en orden.
/// </summary>
public sealed record BloquearEnvio(string Motivo) : AccionRegla;

/// <summary>
/// Regla 15: la ventana de 24h esta cerrada y el envio solo puede salir como plantilla aprobada. Es
/// una senal para quien arma el envio (la respuesta del analista), no un efecto: antes era un evento
/// de la outbox que nadie consumia y quedaba pendiente para siempre (M1, V32).
/// </summary>
public sealed record RequierePlantilla : AccionRegla;

/// <summary>Publica un evento en la outbox para que el Worker o Reporting lo recojan.</summary>
public sealed record PublicarEvento(string Tipo, object Payload) : AccionRegla;

public sealed record RegistrarAuditoria(string Accion, string Detalle) : AccionRegla;

/// <summary>
/// Regla 9: el bot vuelve a preguntar la empresa tras la inactividad configurada, asi que el
/// contexto anterior deja de valer. Sin esto no hay forma de deshacer un EstablecerCuentaContexto.
/// </summary>
public sealed record LimpiarCuentaContexto(string Motivo) : AccionRegla;

/// <summary>Regla 9: sella el recordatorio de 24h para que el proximo barrido no lo repita.</summary>
public sealed record MarcarRecordatorioJobForms(int InvitacionId) : AccionRegla
{
    /// <summary>COR-03: no sellar si el envio anterior no salio. Ver <see cref="AccionRegla"/>.</summary>
    public bool SoloSiSeEnvioAnterior { get; init; }
}

/// <summary>Regla 9: sella el aviso al analista de 48h.</summary>
public sealed record MarcarAvisoAnalistaJobForms(int InvitacionId) : AccionRegla;

/// <summary>Avisos de una conversacion que se sellan para no repetirse (<see cref="SellarConversacion"/>).</summary>
public enum MarcaConversacion
{
    /// <summary>FUN-04: aviso de fuera de horario de este periodo. Sella <c>FechaAvisoFueraHorario</c>.</summary>
    AvisoFueraHorario = 1,

    /// <summary>FUN-05 (A9): aviso a Jefatura por un escalamiento sin respuesta. Sella <c>FechaAvisoSegundoNivel</c>.</summary>
    AvisoSegundoNivel = 2,

    /// <summary>FUN-06 (P3): aviso a Jefatura por un hilo demorado en «Sin clasificar». Sella <c>FechaAvisoPendiente</c>.</summary>
    AvisoPendiente = 3
}

/// <summary>Marcas de una postulacion que se sellan para no repetirse (<see cref="SellarPostulacion"/>).</summary>
public enum MarcaPostulacion
{
    /// <summary>FUN-11 (A14): el analista ya fue avisado de que la postulacion se archiva. Sella <c>FechaAvisoArchivado</c>.</summary>
    AvisoArchivado = 1,

    /// <summary>FUN-10 (A11): el cierre de cortesia salio. Sella <c>FechaCierreCortesia</c> y limpia el pendiente.</summary>
    CierreCortesiaEnviado = 2,

    /// <summary>FUN-10 (A11): el cierre se pidio pero no puede salir todavia (fuera de horario); lo toma el barrido.</summary>
    CierreCortesiaPendiente = 3
}

/// <summary>Resultado de evaluar una regla.</summary>
public sealed record ResultadoRegla(IReadOnlyList<AccionRegla> Acciones, bool DetenerEvaluacion = false)
{
    public static readonly ResultadoRegla SinAccion = new([]);

    public static ResultadoRegla Con(params AccionRegla[] acciones) => new(acciones);

    /// <summary>Para reglas terminales como la 15, que no deben dejar correr a las siguientes.</summary>
    public static ResultadoRegla Detener(params AccionRegla[] acciones) => new(acciones, DetenerEvaluacion: true);
}
