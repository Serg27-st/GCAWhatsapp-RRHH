using RRHH.WhatsApp.Domain.Enums;

namespace RRHH.WhatsApp.Domain.Entidades;

/// <summary>
/// Tabla outbox. Los efectos secundarios (recordatorios, escalamientos, notificaciones, alimentar
/// Reporting) se disparan a partir de estos registros y no de llamadas directas entre modulos,
/// lo que permite reintentar sin perder mensajes.
/// </summary>
public class EventoSistema
{
    public long EventoId { get; set; }
    public required string Tipo { get; set; }
    public required string Payload { get; set; }

    public EstadoEvento Estado { get; set; } = EstadoEvento.Pendiente;

    /// <summary>Un evento que falla repetidamente termina en <see cref="EstadoEvento.Fallido"/> en vez de perderse en silencio.</summary>
    public int IntentosProcesamiento { get; set; }

    public string? UltimoError { get; set; }
    public Guid CorrelationId { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaProcesado { get; set; }
}

/// <summary>
/// Parametros ajustables sin redeploy: las 2h de escalamiento, los 3 dias de repregunta,
/// los 90 dias de archivado, los umbrales de envio. Ver <see cref="ClavesConfiguracion"/>.
/// </summary>
public class ConfiguracionRegla
{
    public required string Clave { get; set; }
    public required string Valor { get; set; }
    public string? Descripcion { get; set; }
    public DateTime FechaActualizacion { get; set; }
}

/// <summary>Claves conocidas de <see cref="ConfiguracionRegla"/>, para no repartir literales por el codigo.</summary>
public static class ClavesConfiguracion
{
    public const string EscalamientoHoras = "escalamiento.horas";

    /// <summary>
    /// Si es true, las horas de escalamiento solo corren dentro del horario de atencion.
    /// Evita que un mensaje recibido al cierre de la jornada escale de madrugada a un
    /// respaldo que tampoco esta disponible (interaccion entre las Reglas 2 y 3).
    /// </summary>
    public const string EscalamientoSoloHorarioLaboral = "escalamiento.solo_horario_laboral";

    public const string RecordatorioJobFormsHoras = "jobforms.recordatorio_horas";
    public const string AvisoAnalistaJobFormsHoras = "jobforms.aviso_analista_horas";
    public const string RepreguntaEmpresaDias = "conversacion.repregunta_dias";
    public const string ArchivadoDias = "conversacion.archivado_dias";
    public const string RetencionCvDias = "datos.retencion_cv_dias";
    public const string VersionAvisoPrivacidad = "datos.version_aviso_privacidad";
    public const string EnvioMaximoPorSegundo = "envio.maximo_por_segundo";

    /// <summary>Intentos de un saliente antes de darlo por perdido y dejarlo visible al analista.</summary>
    public const string EnvioReintentosMaximos = "envio.reintentos_maximos";

    /// <summary>
    /// Base del retroceso exponencial entre reintentos. Crece 1x, 2x, 4x: si Meta esta caido,
    /// insistir cada pocos segundos no adelanta nada y suma trafico a un servicio que ya falla.
    /// </summary>
    public const string EnvioReintentoBaseSegundos = "envio.reintento_base_segundos";
    public const string ReintentosMaximosEvento = "outbox.reintentos_maximos";
}

/// <summary>
/// Trazabilidad de cambios (movimientos de kanban, marcas de blacklist, transferencias).
/// Da soporte a la auditoria que exige la Regla 4.
/// </summary>
public class Auditoria
{
    public long AuditoriaId { get; set; }
    public required string EntidadTipo { get; set; }
    public required string EntidadId { get; set; }
    public int? AnalistaId { get; set; }
    public required string Accion { get; set; }
    public DateTime Fecha { get; set; }
    public string? Detalle { get; set; }
}
