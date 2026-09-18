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

    /// <summary>A9: horas habiles desde el escalamiento, sin respuesta del respaldo, antes de avisar a Jefatura.</summary>
    public const string EscalamientoHorasSegundoNivel = "escalamiento.horas_segundo_nivel";

    /// <summary>P3: horas habiles en «Sin clasificar» antes de avisar a Jefatura. Ningun hilo sin dueño ni plazo.</summary>
    public const string ClasificacionHorasAviso = "clasificacion.horas_aviso";

    /// <summary>R19: reintentos del menu antes de derivar. Reemplaza el literal de la regla (B1).</summary>
    public const string MenuReintentosPermitidos = "menu.reintentos_permitidos";

    /// <summary>A12: horas habiles de silencio tras un texto no reconocido antes de derivar a «Sin clasificar».</summary>
    public const string MenuHorasDerivacion = "menu.horas_derivacion";

    /// <summary>A1: horas habiles hasta que vence una transferencia no urgente sin respuesta.</summary>
    public const string TransferenciaHorasVencimiento = "transferencia.horas_vencimiento";

    /// <summary>A14: dias antes del archivado en que se avisa al analista.</summary>
    public const string ArchivadoAvisoDias = "conversacion.aviso_archivado_dias";

    /// <summary>A11: el cierre de cortesia sale por defecto al descartar; el analista puede marcar «no enviar».</summary>
    public const string CierreAutomatico = "cierre.automatico";

    /// <summary>ARQ-13: dias que se conservan los eventos ya procesados de la outbox antes de purgarlos.</summary>
    public const string OutboxRetencionDiasProcesados = "outbox.retencion_dias_procesados";

    /// <summary>R17: dias de retencion de los adjuntos que llegan por WhatsApp (V33).</summary>
    public const string RetencionAdjuntosDias = "datos.retencion_adjuntos_dias";
}

/// <summary>
/// Algo que una persona tiene que resolver para que los postulantes no se queden sin respuesta: una
/// plantilla sin aprobar, una vacante abierta sin formulario (V32, ARQ-09).
/// <para>
/// Agrupada por (<see cref="Tipo"/>, <see cref="Clave"/>): una fila abierta con contador, no una por
/// ocurrencia. La misma plantilla sin aprobar se dispara por cada postulante que la necesita, y cien
/// filas iguales esconden lo unico que importa. No lleva datos personales: la clave identifica lo que
/// hay que arreglar (<c>hc:12</c>), no a la persona afectada, asi que no entra en la Regla 17.
/// </para>
/// </summary>
public class AlertaOperativa
{
    public int AlertaId { get; set; }

    /// <summary>Uno de <see cref="TiposAlerta"/>.</summary>
    public required string Tipo { get; set; }

    /// <summary>Lo que hay que arreglar, por ejemplo <c>plantilla:recordatorio_24h</c> o <c>hc:12</c>.</summary>
    public required string Clave { get; set; }

    /// <summary>El detalle de la ultima ocurrencia.</summary>
    public string? Detalle { get; set; }

    public int Ocurrencias { get; set; }
    public DateTime FechaPrimera { get; set; }
    public DateTime FechaUltima { get; set; }

    /// <summary>Nula mientras esta abierta. Una ocurrencia posterior a la resolucion abre otra fila.</summary>
    public DateTime? FechaResuelta { get; set; }

    public int? ResueltaPorAnalistaId { get; set; }
}

/// <summary>Tipos de <see cref="AlertaOperativa"/>. Constantes para que quien registra y quien filtra no se separen.</summary>
public static class TiposAlerta
{
    /// <summary>El bot necesito una plantilla que Meta no aprobo: el mensaje no salio.</summary>
    public const string PlantillaNoAprobada = "PlantillaNoAprobada";

    /// <summary>Una vacante abierta sin formulario cargado: el postulante no recibe el enlace.</summary>
    public const string VacanteSinFormulario = "VacanteSinFormulario";

    /// <summary>No hay cuentas con vacantes abiertas: el menu del bot no tiene nada que ofrecer.</summary>
    public const string MenuSinOpciones = "MenuSinOpciones";

    public const string CuentaSinTitular = "CuentaSinTitular";

    public const string CuentaSinRespaldo = "CuentaSinRespaldo";

    public const string CuentaDesactivadaConConversaciones = "CuentaDesactivadaConConversaciones";
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
