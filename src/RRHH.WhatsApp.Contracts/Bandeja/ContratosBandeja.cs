namespace RRHH.WhatsApp.Contracts.Bandeja;

/// <summary>
/// Fila de la lista de conversaciones. Trae lo justo para pintar la bandeja sin abrir el chat:
/// quien escribe, por que cuenta, cuando fue la ultima actividad y si hay algo que atender.
/// </summary>
public sealed record ConversacionResumen(
    int ConversacionId,
    string TelefonoE164,
    string? NombrePostulante,
    string? Dni,
    int? CuentaId,
    string? Cuenta,
    int? AnalistaAtendiendoId,
    string Estado,
    DateTime? FechaUltimoMensajeEntrante,
    DateTime FechaUltimaActividad,
    bool EsperandoRespuesta,
    /// <summary>Regla 15: fuera de la ventana de 24h el analista solo puede responder con plantilla.</summary>
    bool VentanaAbierta,
    /// <summary>Regla 6: otras cuentas donde el mismo DNI esta en proceso. Aviso generico, sin detalle.</summary>
    IReadOnlyList<string> OtrasCuentasEnProceso);

public sealed record MensajeResumen(
    long MensajeId,
    string Direccion,
    string Contenido,
    string? Plantilla,
    int? AnalistaId,
    DateTime FechaEnvio,
    string EstadoEntrega);

/// <summary>
/// El chat completo de un hilo, con su cabecera y sus mensajes.
/// <para>
/// <c>SoloLectura</c> es Sistemas mirando lo que atiende otro (Regla 4): la Api no le va a dejar
/// responder, asi que la bandeja no le ofrece la caja.
/// </para>
/// </summary>
public sealed record ConversacionDetalle(
    ConversacionResumen Resumen,
    IReadOnlyList<MensajeResumen> Mensajes,
    IReadOnlyList<PostulacionResumen> Postulaciones,
    bool SoloLectura);

/// <summary>Tarjeta del tablero kanban (Regla 13).</summary>
public sealed record PostulacionResumen(
    int PostulacionId,
    int PostulanteId,
    string? NombrePostulante,
    string? Dni,
    int HcId,
    string? Vacante,
    int CuentaId,
    int EtapaKanbanId,
    string? Etapa,
    string Estado,
    int? AnalistaAsignadoId,
    DateTime FechaUltimaActividad);

public sealed record TableroKanban(
    int HcId,
    string? Vacante,
    IReadOnlyList<EtapaTablero> Etapas);

public sealed record EtapaTablero(
    int EtapaId,
    string Nombre,
    int Orden,
    IReadOnlyList<PostulacionResumen> Postulaciones);
