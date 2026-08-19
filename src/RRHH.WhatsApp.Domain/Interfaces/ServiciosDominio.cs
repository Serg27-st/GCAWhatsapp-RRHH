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

    /// <summary>Fija sobre que cuenta se esta conversando ahora, tras la eleccion en el menu del bot.</summary>
    Task EstablecerCuentaContextoAsync(int conversacionId, int cuentaId, CancellationToken ct = default);

    /// <summary>Regla 15: deja registrado el consentimiento sin el cual se bloquea todo envio.</summary>
    Task RegistrarOptInAsync(int conversacionId, OrigenOptIn origen, CancellationToken ct = default);

    Task CambiarEstadoAsync(int conversacionId, EstadoConversacion estado, CancellationToken ct = default);

    /// <summary>Bandeja del analista. El rol Sistemas ve todas las conversaciones (Regla 4).</summary>
    Task<IReadOnlyList<Conversacion>> ListarParaAnalistaAsync(int analistaId, CancellationToken ct = default);

    /// <summary>Regla 19: bandeja general de pendientes por clasificar, visible para todos.</summary>
    Task<IReadOnlyList<Conversacion>> ListarPendientesClasificarAsync(CancellationToken ct = default);
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

    /// <summary>Regla 7: whitelist (motivo opcional) o blacklist (motivo obligatorio).</summary>
    Task MarcarEstadoAsync(
        int postulanteId, int cuentaId, TipoEstadoPostulante tipo,
        string? motivo, int analistaId, CancellationToken ct = default);

    /// <summary>Tablero completo de una vacante, agrupado por etapa.</summary>
    Task<IReadOnlyList<Postulacion>> ObtenerTableroAsync(int hcId, CancellationToken ct = default);

    /// <summary>Regla 6: otras cuentas en las que el mismo DNI esta en proceso, para el aviso generico.</summary>
    Task<IReadOnlyList<Cuenta>> ObtenerOtrasCuentasEnProcesoAsync(
        int postulanteId, int cuentaExcluidaId, CancellationToken ct = default);
}

public interface IPostulanteService
{
    Task<Postulante?> BuscarPorDniAsync(string dni, CancellationToken ct = default);

    Task<Postulante> RegistrarDesdeFormularioAsync(JobFormsRespuesta respuesta, CancellationToken ct = default);

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
}

/// <summary>Regla 14: se consulta antes de asignar una conversacion.</summary>
public interface IAusenciaService
{
    Task<Ausencia> RegistrarAsync(int analistaId, DateTime inicio, DateTime fin, string? motivo, CancellationToken ct = default);

    Task<bool> EstaAusenteAsync(int analistaId, DateTime momento, CancellationToken ct = default);
}

/// <summary>Regla 3: valida si un mensaje entrante llega dentro del horario laboral configurado.</summary>
public interface IHorarioAtencionService
{
    Task<bool> EstaEnHorarioAsync(int? cuentaId, DateTime momentoUtc, CancellationToken ct = default);

    /// <summary>Texto del horario vigente, para el parametro de la plantilla de fuera de horario.</summary>
    Task<string> DescribirHorarioAsync(int? cuentaId, CancellationToken ct = default);
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

/// <summary>Lee la configuracion parametrizable de las reglas, sin obligar a redeploy para ajustarla.</summary>
public interface IConfiguracionReglasService
{
    Task<IReadOnlyDictionary<string, string>> ObtenerTodasAsync(CancellationToken ct = default);

    Task EstablecerAsync(string clave, string valor, CancellationToken ct = default);
}

/// <summary>Outbox. Los efectos secundarios se publican aca y el Worker los consume.</summary>
public interface IEventoSistemaService
{
    Task PublicarAsync(string tipo, object payload, Guid correlationId, CancellationToken ct = default);

    Task<IReadOnlyList<EventoSistema>> ObtenerPendientesAsync(int maximo, CancellationToken ct = default);

    Task MarcarProcesadoAsync(long eventoId, CancellationToken ct = default);

    Task MarcarFallidoAsync(long eventoId, string error, int reintentosMaximos, CancellationToken ct = default);
}
