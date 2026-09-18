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
/// <param name="Tandas">
/// FUN-17 (M7): cada vez que el postulante escribió y quedó esperando. Es la unidad real del
/// servicio: un hilo que va y viene tres veces son tres esperas, no una.
/// </param>
/// <param name="MinutosHabilesPromedio">
/// A15: medido con el horario de atención, como el plazo de la Regla 2. A reloj corrido, un mensaje
/// del viernes a la tarde respondido el lunes da tres días y dice que nadie contesta.
/// </param>
/// <param name="PorcentajeDentroDelPlazo">Tandas respondidas dentro del plazo de escalamiento, entre 0 y 1.</param>
public sealed record MetricasRespuesta(
    int ConversacionesConRespuesta,
    int ConversacionesSinResponder,
    double? MinutosPromedio,
    /// <summary>La mediana acompana al promedio porque un solo caso de fin de semana lo distorsiona.</summary>
    double? MinutosMediana,
    /// <summary>Respondidas dentro del plazo de escalamiento de la Regla 2.</summary>
    int DentroDelPlazo,
    int Escalamientos,
    int Tandas,
    int TandasRespondidas,
    double? MinutosHabilesPromedio,
    double? MinutosHabilesMediana,
    double PorcentajeDentroDelPlazo);

/// <summary>Tasa de conversion postulante → contratado sobre las postulaciones del periodo.</summary>
public sealed record MetricasConversion(
    int Postulaciones,
    int Contratados,
    int Descartados,
    int EnProceso,
    double TasaConversion);

/// <param name="MinutosHabilesPromedioRespuesta">
/// FUN-17: promedio de sus tandas, en horas habiles. Es la misma vara con la que se mide el panel.
/// </param>
public sealed record ActividadAnalista(
    int AnalistaId,
    string Nombre,
    int ConversacionesAtendidas,
    int MensajesEnviados,
    double? MinutosPromedioPrimeraRespuesta,
    int Contratados,
    int Descartados,
    double? MinutosHabilesPromedioRespuesta);

public sealed record MetricasCuenta(
    int CuentaId,
    string Nombre,
    int Postulaciones,
    int Contratados,
    double TasaConversion);
