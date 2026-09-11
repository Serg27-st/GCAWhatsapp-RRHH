namespace RRHH.WhatsApp.Contracts.Metricas;

/// <summary>
/// Panel de gerencia (Regla 18). Reutiliza lo que ya se registra en conversaciones, mensajes y
/// postulaciones: no hay ningun dato que alguien tenga que cargar a mano para que estos numeros
/// existan.
/// </summary>
public sealed record MetricasGerencia(
    DateTime DesdeUtc,
    DateTime HastaUtc,
    MetricasRespuesta Respuesta,
    MetricasConversion Conversion,
    IReadOnlyList<ActividadAnalista> Analistas,
    IReadOnlyList<MetricasCuenta> Cuentas);

/// <summary>
/// Tiempo de primera respuesta. Cuenta solo la respuesta de una persona: los mensajes del bot son
/// automaticos y medirlos daria un promedio de segundos que no dice nada del servicio.
/// </summary>
public sealed record MetricasRespuesta(
    int ConversacionesConRespuesta,
    int ConversacionesSinResponder,
    double? MinutosPromedio,
    /// <summary>La mediana acompana al promedio porque un solo caso de fin de semana lo distorsiona.</summary>
    double? MinutosMediana,
    /// <summary>Respondidas dentro del plazo de escalamiento de la Regla 2.</summary>
    int DentroDelPlazo,
    int Escalamientos);

/// <summary>Tasa de conversion postulante → contratado sobre las postulaciones del periodo.</summary>
public sealed record MetricasConversion(
    int Postulaciones,
    int Contratados,
    int Descartados,
    int EnProceso,
    double TasaConversion);

public sealed record ActividadAnalista(
    int AnalistaId,
    string Nombre,
    int ConversacionesAtendidas,
    int MensajesEnviados,
    double? MinutosPromedioPrimeraRespuesta,
    int Contratados,
    int Descartados);

public sealed record MetricasCuenta(
    int CuentaId,
    string Nombre,
    int Postulaciones,
    int Contratados,
    double TasaConversion);
