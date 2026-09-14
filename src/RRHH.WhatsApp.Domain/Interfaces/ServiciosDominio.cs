using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;

namespace RRHH.WhatsApp.Domain.Interfaces;

/// <summary>
/// Unico punto de acceso a la tabla de Conversaciones. El resto del sistema pasa por aca y
/// nunca toca sus tablas directamente.
/// </summary>
public interface IConversacionService
{
    /// <summary>Busca el hilo por telefono o lo crea. El postulante puede no existir aun: el DNI llega despues.</summary>
    Task<Conversacion> ObtenerOCrearAsync(string telefonoE164, CancellationToken ct = default);

    Task<Conversacion?> ObtenerPorIdAsync(int conversacionId, CancellationToken ct = default);

    /// <summary>
    /// Actualiza el hilo tras un mensaje entrante: abre la ventana de 24h, refresca la actividad
    /// y registra el opt-in si es la primera vez (Regla 15).
    /// </summary>
    Task RegistrarEntradaAsync(int conversacionId, DateTime fechaUtc, CancellationToken ct = default);

    /// <summary>Regla 1: enruta la conversacion al analista dueno de la cuenta identificada.</summary>
    Task AsignarAnalistaAsync(int conversacionId, int analistaId, string motivo, CancellationToken ct = default);

    /// <summary>Regla 2 y 14: pasa la conversacion al respaldo fijo de la cuenta.</summary>
    Task EscalarAsync(int conversacionId, int analistaRespaldoId, string motivo, CancellationToken ct = default);

    /// <summary>Regla 8: deriva a otro analista, de uno en uno.</summary>
    Task<Transferencia> TransferirAsync(
        int conversacionId, int analistaOrigenId, int analistaDestinoId,
        bool urgente, string? comentario, CancellationToken ct = default);

    Task ResponderTransferenciaAsync(
        int transferenciaId, int analistaDestinoId, bool aceptada, CancellationToken ct = default);

    /// <summary>
    /// Regla 8: transferencias esperando la respuesta de un analista.
    /// <para>
    /// Sin esto el destino recibe el aviso de que le transfirieron algo pero no tiene dónde verlo,
    /// y una transferencia que requiere aceptación se queda Pendiente para siempre.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<Transferencia>> ListarTransferenciasPendientesAsync(
        int analistaDestinoId, CancellationToken ct = default);

    /// <summary>Fija sobre que cuenta se esta conversando ahora, tras la eleccion en el menu del bot.</summary>
    Task EstablecerCuentaContextoAsync(int conversacionId, int cuentaId, CancellationToken ct = default);

    /// <summary>
    /// Regla 9: descarta la cuenta en contexto cuando el postulante reaparece tras varios dias,
    /// para que el bot pueda volver a preguntarle la empresa.
    /// </summary>
    Task LimpiarCuentaContextoAsync(int conversacionId, string motivo, CancellationToken ct = default);

    /// <summary>
    /// Regla 9: enlaza el hilo con la persona una vez que el DNI llega por el formulario. Hasta
    /// ese momento la conversacion solo conoce un telefono (desviacion V2 de docs/decisiones.md).
    /// </summary>
    Task VincularPostulanteAsync(int conversacionId, int postulanteId, CancellationToken ct = default);

    /// <summary>Regla 15: deja registrado el consentimiento sin el cual se bloquea todo envio.</summary>
    Task RegistrarOptInAsync(int conversacionId, OrigenOptIn origen, CancellationToken ct = default);

    /// <summary>
    /// Marca que un analista respondio. Es lo que detiene el reloj de la Regla 2: sin esto, el
    /// Worker escalaria conversaciones que ya fueron atendidas.
    /// </summary>
    Task RegistrarRespuestaAnalistaAsync(int conversacionId, DateTime fechaUtc, CancellationToken ct = default);

    Task CambiarEstadoAsync(int conversacionId, EstadoConversacion estado, CancellationToken ct = default);

    /// <summary>Bandeja del analista. El rol Sistemas ve todas las conversaciones (Regla 4).</summary>
    Task<IReadOnlyList<Conversacion>> ListarParaAnalistaAsync(int analistaId, CancellationToken ct = default);

    /// <summary>
    /// Buscador por DNI de la Seccion 7: trae el hilo del postulante, si ya se identifico. Respeta
    /// la Regla 4 igual que la bandeja: un analista encuentra lo que atiende; Sistemas, todo.
    /// </summary>
    Task<IReadOnlyList<Conversacion>> BuscarPorDniAsync(
        string dni, int analistaId, CancellationToken ct = default);

    /// <summary>
    /// Regla 4: que puede hacer el analista con este hilo. La Api lo consulta antes de cualquier
    /// accion sobre una conversacion: tener sesion no alcanza para leer o responder una ajena.
    /// </summary>
    Task<NivelAcceso> ObtenerAccesoAsync(int conversacionId, int analistaId, CancellationToken ct = default);

    /// <summary>Regla 19: bandeja general de pendientes por clasificar, visible para todos.</summary>
    Task<IReadOnlyList<Conversacion>> ListarPendientesClasificarAsync(CancellationToken ct = default);

    /// <summary>
    /// Regla 2: hilos con un mensaje del postulante todavia sin responder, que son los candidatos
    /// a escalar. Devuelve solo los identificadores porque el barrido recarga cada conversacion en
    /// su propio ambito; y decide la regla, no esta consulta, si las 2 horas ya se cumplieron.
    /// </summary>
    Task<IReadOnlyList<int>> ListarPendientesEscalamientoAsync(int maximo, CancellationToken ct = default);

    /// <summary>
    /// Regla 16: hilos sin actividad desde hace mas de <paramref name="diasSinActividad"/> y que
    /// todavia no estan archivados. Igual que en el escalamiento, esto solo prefiltra candidatos:
    /// si corresponde archivar o no lo decide la regla, que es la que mira las postulaciones.
    /// </summary>
    Task<IReadOnlyList<int>> ListarPendientesArchivadoAsync(
        int diasSinActividad, int maximo, CancellationToken ct = default);
}


/// <summary>
/// Catalogo de analistas. La bandeja lo necesita para ofrecer destinos de transferencia (Regla 8)
/// y para saber quien tiene el rol Sistemas, que ve todo (Regla 4).
/// </summary>
public interface IAnalistaService
{
    Task<IReadOnlyList<Analista>> ListarActivosAsync(CancellationToken ct = default);

    Task<Analista?> ObtenerPorIdAsync(int analistaId, CancellationToken ct = default);

    /// <summary>Alta de analista. El email es unico y es lo que lo identificara cuando haya login.</summary>
    Task<Analista> CrearAsync(
        string nombre, string email, RolAnalista rol, CancellationToken ct = default);
}

/// <summary>
/// Señal de vida de los procesos en segundo plano (Sección 9.6.2). El Worker la escribe al cerrar
/// cada ciclo; el health de la Api la lee para saber si sigue trabajando.
/// </summary>
public interface ILatidoServicio
{
    /// <summary>Deja constancia de que el bucle acaba de completar un ciclo.</summary>
    Task RegistrarAsync(
        string servicio, TimeSpan tolerancia, string? detalle = null, CancellationToken ct = default);

    /// <summary>Último latido de cada bucle vigilado.</summary>
    Task<IReadOnlyList<LatidoServicio>> ListarAsync(CancellationToken ct = default);
}

/// <summary>Lo que devuelve un intento de inicio de sesión.</summary>
public sealed record ResultadoAutenticacion(
    bool Exito, string? Motivo, int AnalistaId, string? Nombre, string? Rol);

/// <summary>
/// Autenticación de analistas (Sección 9.6.1). Sin esto la Regla 4 —cada analista ve solo lo
/// suyo— no tiene efecto real: cualquiera que alcance la Api leería conversaciones ajenas.
/// </summary>
public interface IAutenticacionService
{
    Task<ResultadoAutenticacion> VerificarAsync(
        string email, string contrasena, CancellationToken ct = default);

    /// <summary>Fija o cambia la contraseña de un analista.</summary>
    Task EstablecerContrasenaAsync(int analistaId, string contrasena, CancellationToken ct = default);

    /// <summary>
    /// Si ningún analista tiene contraseña todavía. Es lo que habilita el arranque inicial: una vez
    /// que existe la primera, esa puerta se cierra sola y para siempre.
    /// </summary>
    Task<bool> SinContrasenasAsync(CancellationToken ct = default);
}
/// <summary>Catalogo de cuentas y vacantes, que es lo que alimenta el menu del bot.</summary>
public interface ICuentaService
{
    /// <summary>
    /// Cuentas activas que hoy tienen al menos una vacante abierta. Una cuenta sin vacantes no
    /// debe aparecer en el menu: llevaria al postulante a un callejon sin salida (Regla 20).
    /// </summary>
    Task<IReadOnlyList<Cuenta>> ListarConVacantesAbiertasAsync(CancellationToken ct = default);

    Task<Cuenta?> ObtenerPorIdAsync(int cuentaId, CancellationToken ct = default);

    Task<IReadOnlyList<Hc>> ListarVacantesAbiertasAsync(int cuentaId, CancellationToken ct = default);

    /// <summary>Una vacante por id, abierta o cerrada. Nula si no existe.</summary>
    Task<Hc?> ObtenerVacanteAsync(int hcId, CancellationToken ct = default);

    /// <summary>
    /// Alta de vacante. Sin esto el estado de un HC nunca cambiaria y la Regla 20 no tendria
    /// como activarse en la practica.
    /// </summary>
    Task<Hc> CrearVacanteAsync(
        int cuentaId, string titulo, string? urlJobForms, int analistaId, CancellationToken ct = default);

    /// <summary>Regla 20: cerrar la vacante desactiva su enlace y hace que el bot deje de ofrecerla.</summary>
    Task CerrarVacanteAsync(int hcId, int analistaId, CancellationToken ct = default);

    /// <summary>
    /// Campos opcionales configurados para una vacante. Es lo que el JobForms consulta para saber
    /// que preguntar de mas, ademas de los campos fijos (Seccion 9.2).
    /// </summary>
    Task<IReadOnlyList<HcCampoOpcional>> ListarCamposOpcionalesAsync(
        int hcId, CancellationToken ct = default);

    /// <summary>
    /// Reemplaza el catalogo de campos opcionales de una vacante.
    /// <para>
    /// Es reemplazo y no edicion campo por campo porque el analista los define como un conjunto al
    /// abrir la vacante; ir agregando de a uno deja el formulario en estados intermedios que nadie
    /// configuro.
    /// </para>
    /// </summary>
    Task ReemplazarCamposOpcionalesAsync(
        int hcId, IReadOnlyList<HcCampoOpcional> campos, int analistaId, CancellationToken ct = default);

    /// <summary>
    /// Cuentas asignadas a un analista y en cuales es respaldo. Es lo que la bandeja usa para
    /// agrupar por subdivision (Seccion 7) sin tocar SQL Server.
    /// </summary>
    Task<IReadOnlyList<(Cuenta Cuenta, bool EsBackup, int VacantesAbiertas)>> ListarDeAnalistaAsync(
        int analistaId, CancellationToken ct = default);

    /// <summary>
    /// Regla 4 por cuenta: el titular y el respaldo trabajan lo de su cuenta —el tablero, las
    /// tarjetas—; Sistemas lo ve sin actuar; el resto no lo ve.
    /// </summary>
    Task<NivelAcceso> ObtenerAccesoAsync(int cuentaId, int analistaId, CancellationToken ct = default);

    /// <summary>Alta de cliente. El nombre es unico: es como el analista lo reconoce en la bandeja.</summary>
    Task<Cuenta> CrearAsync(string nombre, CancellationToken ct = default);

    /// <summary>Catalogo completo, inactivas incluidas: la administracion necesita verlas todas.</summary>
    Task<IReadOnlyList<Cuenta>> ListarTodasAsync(CancellationToken ct = default);

    /// <summary>
    /// Cada cuenta con su titular, su respaldo y cuantas vacantes tiene abiertas. Es la vista que
    /// hace falta para saber si la operacion esta lista: una cuenta sin titular no enruta a nadie.
    /// </summary>
    Task<IReadOnlyList<(Cuenta Cuenta, Analista? Titular, Analista? Respaldo, int VacantesAbiertas)>>
        ListarConDotacionAsync(CancellationToken ct = default);

    /// <summary>
    /// Regla 1 y Regla 2: pone al analista como titular o como respaldo de la cuenta. Cada cuenta
    /// admite un titular y un respaldo, y no pueden ser la misma persona: si lo fueran, escalar
    /// devolveria la conversacion a quien ya no respondio.
    /// </summary>
    Task AsignarAnalistaAsync(
        int cuentaId, int analistaId, bool esBackup, CancellationToken ct = default);

    Task QuitarAnalistaAsync(int cuentaId, int analistaId, CancellationToken ct = default);
}

/// <summary>Unico punto de acceso a la tabla de Mensajes.</summary>
public interface IMensajeService
{
    /// <summary>
    /// Persiste un mensaje entrante. Devuelve <c>null</c> si el <c>ProviderMessageId</c> ya existia:
    /// Meta reintenta la entrega y el mismo mensaje no puede procesarse dos veces (Seccion 9.6.2).
    /// </summary>
    Task<Mensaje?> RegistrarEntranteAsync(
        int conversacionId, MensajeEntranteDto dto, Guid correlationId, CancellationToken ct = default);

    /// <summary>
    /// Deja constancia de un mensaje que se envio, para que la bandeja muestre el hilo completo.
    /// <paramref name="parametrosPlantilla"/> se guarda para que un reintento pueda rearmar la
    /// plantilla: el contenido almacenado conserva los {{n}} sin reemplazar.
    /// </summary>
    Task<Mensaje> RegistrarSalienteAsync(
        int conversacionId, string contenido, int? plantillaId, int? analistaId,
        string? providerMessageId, Guid correlationId,
        IReadOnlyList<string>? parametrosPlantilla = null, CancellationToken ct = default);

    /// <summary>Aplica un acuse de entrega. Ignora los acuses de mensajes que no conocemos.</summary>
    Task ActualizarEstadoEntregaAsync(EstadoEntregaDto dto, CancellationToken ct = default);

    /// <summary>
    /// Marca un saliente que nunca llego a salir. Va por <c>MensajeId</c> y no por el id del
    /// proveedor porque justamente no hay: un envio rechazado no devuelve ninguno, y buscarlo por
    /// un id inventado deja el fallo sin registrar en la base (Seccion 9.6.2).
    /// <para>
    /// <paramref name="proximoIntentoUtc"/> nulo significa que no se va a reintentar: o el fallo
    /// es permanente, o es ambiguo, o se agotaron los intentos.
    /// </para>
    /// </summary>
    Task MarcarEnvioFallidoAsync(
        long mensajeId, string? error, ClaseFallo clase = ClaseFallo.Permanente,
        DateTime? proximoIntentoUtc = null, CancellationToken ct = default);

    /// <summary>
    /// Salientes que fallaron por causa transitoria y a los que ya les toca otro intento.
    /// Devuelve la conversacion incluida porque el reintento tiene que revalidar la Regla 15 antes
    /// de volver a enviar.
    /// </summary>
    Task<IReadOnlyList<Mensaje>> ListarPendientesDeReintentoAsync(
        int maximo, DateTime ahoraUtc, CancellationToken ct = default);

    /// <summary>Un reintento que salio bien: queda el id del proveedor y no vuelve a intentarse.</summary>
    Task MarcarEnvioLogradoAsync(long mensajeId, string? providerMessageId, CancellationToken ct = default);

    Task<IReadOnlyList<Mensaje>> ListarPorConversacionAsync(
        int conversacionId, int maximo = 100, CancellationToken ct = default);
}

/// <summary>
/// Postulaciones: la persona aplicando a una vacante concreta. Es lo que recorre el tablero kanban.
/// Se separo de <see cref="IConversacionService"/> porque el kanban es por vacante (Regla 13) y una
/// misma conversacion de WhatsApp puede sostener varias postulaciones (Regla 6).
/// </summary>
public interface IPostulacionService
{
    Task<Postulacion> CrearAsync(int postulanteId, int hcId, CancellationToken ct = default);

    /// <summary>Regla 13: mueve la postulacion entre columnas del tablero.</summary>
    Task MoverEtapaKanbanAsync(int postulacionId, int etapaId, int analistaId, CancellationToken ct = default);

    /// <summary>
    /// Regla 7: whitelist (motivo opcional) o blacklist (motivo obligatorio).
    /// <para>
    /// Devuelve las postulaciones que quedaron descartadas por la marca, que son las que
    /// disparan el cierre de cortesia de la Regla 12.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<int>> MarcarEstadoAsync(
        int postulanteId, int cuentaId, TipoEstadoPostulante tipo,
        string? motivo, int analistaId, CancellationToken ct = default);

    /// <summary>Postulaciones de una persona, para mostrar su historial junto al chat.</summary>
    Task<IReadOnlyList<Postulacion>> ObtenerTableroPorPostulanteAsync(
        int postulanteId, CancellationToken ct = default);

    /// <summary>Una postulacion por id, sin seguimiento. Nula si no existe.</summary>
    Task<Postulacion?> ObtenerPorIdAsync(int postulacionId, CancellationToken ct = default);

    /// <summary>Desenlace actual de una postulacion. Nulo si no existe.</summary>
    Task<EstadoPostulacion?> ObtenerEstadoAsync(int postulacionId, CancellationToken ct = default);

    /// <summary>Tablero completo de una vacante, agrupado por etapa.</summary>
    Task<IReadOnlyList<Postulacion>> ObtenerTableroAsync(int hcId, CancellationToken ct = default);

    /// <summary>
    /// Columnas del tablero, en orden. El tablero las necesita completas y no solo las que hoy
    /// tienen tarjetas: una etapa vacia sigue siendo una columna a la que se puede arrastrar.
    /// </summary>
    Task<IReadOnlyList<EtapaKanban>> ListarEtapasAsync(CancellationToken ct = default);

    /// <summary>Regla 6: otras cuentas en las que el mismo DNI esta en proceso, para el aviso generico.</summary>
    Task<IReadOnlyList<Cuenta>> ObtenerOtrasCuentasEnProcesoAsync(
        int postulanteId, int cuentaExcluidaId, CancellationToken ct = default);
}

public interface IPostulanteService
{
    Task<Postulante?> BuscarPorDniAsync(string dni, CancellationToken ct = default);

    /// <summary>Crea el postulante o actualiza el que ya existe con ese DNI (Regla 9).</summary>
    Task<Postulante> RegistrarDesdeFormularioAsync(
        DatosPostulanteFormulario datos, CancellationToken ct = default);

    /// <summary>El flujo muestra el historial previo cuando el DNI ya existe.</summary>
    Task<IReadOnlyList<Postulacion>> ObtenerHistorialAsync(string dni, CancellationToken ct = default);

    /// <summary>
    /// Regla 17: borrado efectivo de datos personales ante una solicitud del postulante.
    /// Anonimiza en vez de eliminar filas, para no romper las metricas historicas de la Regla 18.
    /// </summary>
    Task AnonimizarDatosAsync(string dni, string motivo, CancellationToken ct = default);
}

public interface IJobFormsService
{
    Task<JobFormsRespuesta> ValidarEnvioAsync(JobFormsRespuesta respuesta, CancellationToken ct = default);

    Task<string> AlmacenarCvAsync(Stream contenido, string nombreArchivo, string contentType, CancellationToken ct = default);

    /// <summary>Regla 20: la vacante sigue abierta y su enlace es valido.</summary>
    Task<bool> ValidarVacanteActivaAsync(int hcId, CancellationToken ct = default);

    /// <summary>Regla 17: el postulante acepto el aviso de privacidad vigente.</summary>
    Task<bool> ValidarConsentimientoAsync(JobFormsRespuesta respuesta, CancellationToken ct = default);

    /// <summary>
    /// La respuesta ya guardada de esa invitacion, si existe. Es lo que permite reconocer un envio
    /// repetido sin volver a procesarlo (V27).
    /// </summary>
    Task<JobFormsRespuesta?> ObtenerRespuestaDeInvitacionAsync(int invitacionId, CancellationToken ct = default);

    /// <summary>
    /// Regla 17: respuestas cuyo CV ya cumplio el plazo de retencion. Es lo que consume el job de
    /// purga del Worker (Seccion 9.6.5).
    /// </summary>
    Task<IReadOnlyList<JobFormsRespuesta>> ListarCvsPorPurgarAsync(
        int diasRetencion, int maximo, CancellationToken ct = default);

    /// <summary>Borra el CV y limpia su referencia, dejando constancia en la auditoria.</summary>
    Task PurgarCvAsync(int respuestaId, CancellationToken ct = default);
}

/// <summary>
/// Seguimiento del link de JobForms. Es lo que el Worker consulta para disparar el recordatorio
/// de 24h y el aviso al analista de 48h (Regla 9).
/// </summary>
public interface IJobFormsInvitacionService
{
    Task<JobFormsInvitacion> CrearInvitacionAsync(int conversacionId, int hcId, CancellationToken ct = default);

    Task<JobFormsInvitacion?> ObtenerPorTokenAsync(Guid token, CancellationToken ct = default);

    Task MarcarRecordatorioEnviadoAsync(int invitacionId, CancellationToken ct = default);

    Task MarcarAvisoAnalistaEnviadoAsync(int invitacionId, CancellationToken ct = default);

    Task MarcarCompletadoAsync(int invitacionId, CancellationToken ct = default);

    Task<IReadOnlyList<JobFormsInvitacion>> ListarPendientesRecordatorioAsync(TimeSpan antiguedad, CancellationToken ct = default);

    Task<IReadOnlyList<JobFormsInvitacion>> ListarPendientesAvisoAnalistaAsync(TimeSpan antiguedad, CancellationToken ct = default);
}

/// <summary>
/// Resuelve que plantilla aprobada por Meta usar y si la ventana de 24h sigue abierta.
/// Sin esta pieza, las Reglas 3, 9, 12 y 15 no tienen quien las ejecute.
/// </summary>
public interface IPlantillaService
{
    Task<Plantilla?> ObtenerPlantillaParaEventoAsync(string clave, CancellationToken ct = default);

    /// <summary>true si se puede enviar texto libre; false si el envio debe ir como plantilla.</summary>
    Task<bool> ValidarVentana24hAsync(int conversacionId, CancellationToken ct = default);

    Task<IReadOnlyList<Plantilla>> ListarActivasAsync(CancellationToken ct = default);

    /// <summary>Catalogo completo, incluidas las inactivas: soporte necesita ver que falta aprobar.</summary>
    Task<IReadOnlyList<Plantilla>> ListarTodasAsync(CancellationToken ct = default);
}

/// <summary>Regla 14: se consulta antes de asignar una conversacion.</summary>
public interface IAusenciaService
{
    Task<Ausencia> RegistrarAsync(int analistaId, DateTime inicio, DateTime fin, string? motivo, CancellationToken ct = default);

    Task<bool> EstaAusenteAsync(int analistaId, DateTime momento, CancellationToken ct = default);

    /// <summary>Las que terminan despues de <paramref name="desdeUtc"/>: en curso y programadas.</summary>
    Task<IReadOnlyList<Ausencia>> ListarVigentesAsync(int analistaId, DateTime desdeUtc, CancellationToken ct = default);

    /// <summary>Cancela una ausencia de ese analista. False si no existe o es de otro.</summary>
    Task<bool> EliminarAsync(int analistaId, int ausenciaId, CancellationToken ct = default);
}

/// <summary>Regla 3: valida si un mensaje entrante llega dentro del horario laboral configurado.</summary>
public interface IHorarioAtencionService
{
    Task<bool> EstaEnHorarioAsync(int? cuentaId, DateTime momentoUtc, CancellationToken ct = default);

    /// <summary>Texto del horario vigente, para el parametro de la plantilla de fuera de horario.</summary>
    Task<string> DescribirHorarioAsync(int? cuentaId, CancellationToken ct = default);

    /// <summary>
    /// Minutos de horario laboral transcurridos entre dos instantes. Es lo que permite que el
    /// escalamiento de la Regla 2 no corra de madrugada: el calendario se resuelve aca y la
    /// decision de que hacer con el numero se queda en la regla.
    /// </summary>
    Task<double> MinutosHabilesEntreAsync(
        int? cuentaId, DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct = default);

    /// <summary>Tramos configurados para una cuenta, o los generales si <paramref name="cuentaId"/> es nulo.</summary>
    Task<IReadOnlyList<HorarioAtencion>> ObtenerTramosAsync(int? cuentaId, CancellationToken ct = default);

    /// <summary>
    /// Reemplaza el horario completo de una cuenta. Es reemplazo y no edicion parcial porque el
    /// horario se piensa como una semana entera: dejar tramos viejos mezclados con nuevos produce
    /// jornadas que nadie configuro.
    /// </summary>
    Task ReemplazarTramosAsync(
        int? cuentaId, IReadOnlyList<HorarioAtencion> tramos, int analistaId, CancellationToken ct = default);
}

/// <summary>
/// Almacenamiento de CVs fuera de SQL Server (Seccion 8.3). En el despliegue on-premise la
/// implementacion escribe en un recurso compartido; la interfaz deja la puerta abierta a
/// mover los archivos a almacenamiento de objetos sin tocar el resto del sistema.
/// </summary>
public interface IAlmacenamientoCv
{
    Task<string> GuardarAsync(Stream contenido, string nombreArchivo, string contentType, CancellationToken ct = default);

    Task<Stream?> ObtenerAsync(string ruta, CancellationToken ct = default);

    Task EliminarAsync(string ruta, CancellationToken ct = default);
}

/// <summary>Qué dijo el antivirus sobre un archivo.</summary>
public enum ResultadoEscaneo
{
    /// <summary>El escaneo corrió y no encontró nada.</summary>
    Limpio = 1,

    /// <summary>El escaneo corrió y encontró algo.</summary>
    Amenaza = 2,

    /// <summary>
    /// No se pudo escanear: el antivirus está apagado, no está instalado o no respondió a tiempo.
    /// Se distingue de <see cref="Limpio"/> a propósito — "no sé" no es "está bien", y quien
    /// llama decide si eso alcanza para aceptar el archivo.
    /// </summary>
    NoDisponible = 3
}

public sealed record VeredictoEscaneo(ResultadoEscaneo Resultado, string? Detalle);

/// <summary>
/// Escaneo antivirus de los adjuntos (Sección 9.6.1). El endpoint del JobForms es el único
/// alcanzable desde internet sin autenticación, así que es por donde entraría un archivo hostil.
/// </summary>
public interface IEscanerAntivirus
{
    Task<VeredictoEscaneo> EscanearAsync(string rutaArchivo, CancellationToken ct = default);
}

/// <summary>Lee la configuracion parametrizable de las reglas, sin obligar a redeploy para ajustarla.</summary>
public interface IConfiguracionReglasService
{
    Task<IReadOnlyDictionary<string, string>> ObtenerTodasAsync(CancellationToken ct = default);

    /// <summary>Los parametros con su descripcion, para administrarlos. Sin cache: se pide rara vez.</summary>
    Task<IReadOnlyList<ConfiguracionRegla>> ListarAsync(CancellationToken ct = default);

    Task EstablecerAsync(string clave, string valor, CancellationToken ct = default);
}

/// <summary>
/// Trazabilidad de cambios (Regla 4). Se registra desde un solo lugar para que el formato de las
/// entradas sea consistente y la auditoria se pueda leer.
/// </summary>
public interface IAuditoriaService
{
    Task RegistrarAsync(
        string entidadTipo, string entidadId, int? analistaId,
        string accion, string? detalle, CancellationToken ct = default);

    Task<IReadOnlyList<Auditoria>> ListarPorEntidadAsync(
        string entidadTipo, string entidadId, CancellationToken ct = default);
}

/// <summary>Outbox. Los efectos secundarios se publican aca y el Worker los consume.</summary>
public interface IEventoSistemaService
{
    Task PublicarAsync(string tipo, object payload, Guid correlationId, CancellationToken ct = default);

    /// <summary>
    /// Pendientes de los <paramref name="tipos"/> indicados, del mas antiguo al mas nuevo.
    /// <para>
    /// El filtro por tipo no es comodidad: la outbox tiene varios consumidores previstos y no todos
    /// existen todavia. Sin el, los eventos que aun no tienen duenno se acumularian a la cabeza de
    /// la cola y taparian a los que si se pueden procesar. Una lista vacia devuelve todos los tipos.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<EventoSistema>> ObtenerPendientesAsync(
        int maximo, IReadOnlyCollection<string> tipos, CancellationToken ct = default);

    Task MarcarProcesadoAsync(long eventoId, CancellationToken ct = default);

    Task MarcarFallidoAsync(long eventoId, string error, int reintentosMaximos, CancellationToken ct = default);
}
