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
    IReadOnlyList<string> OtrasCuentasEnProceso,
    /// <summary>
    /// FUN-05 (A9): el hilo se escalo, Jefatura ya fue avisada y el postulante sigue esperando. La
    /// bandeja lo marca para que se vea sin abrirlo.
    /// </summary>
    bool Vencida = false);

public sealed record MensajeResumen(
    long MensajeId,
    string Direccion,
    string Contenido,
    string? Plantilla,
    int? AnalistaId,
    DateTime FechaEnvio,
    /// <summary>Uno de <see cref="EstadosEntrega"/>.</summary>
    string EstadoEntrega,
    /// <summary>FUN-14: los archivos que llegaron con el mensaje. Vacia en casi todos.</summary>
    IReadOnlyList<AdjuntoResumen> Adjuntos,
    /// <summary>FUN-13 (M2): por que no llego, tal como lo informo el proveedor. Nulo si no fallo.</summary>
    string? Error = null,
    /// <summary>
    /// FUN-13: un texto de un analista que el proveedor rechazo en firme. Reenviarlo no lo duplica,
    /// porque no llego; un fallo ambiguo pudo haber salido y uno transitorio ya lo reintenta el Worker.
    /// </summary>
    bool Reintentable = false);

/// <summary>
/// FUN-14 (V33): un archivo que mando el postulante. Solo el que esta en
/// <see cref="EstadosAdjunto.Descargado"/> se puede bajar: lo demas todavia no paso el antivirus, no
/// lo paso o ya se borro por retencion.
/// </summary>
/// <param name="Tipo"><c>image</c>, <c>document</c>, <c>audio</c>, <c>video</c> o <c>sticker</c>.</param>
/// <param name="NombreArchivo">El que puso la persona. Solo los documentos lo traen.</param>
/// <param name="Estado">Uno de <see cref="EstadosAdjunto"/>.</param>
public sealed record AdjuntoResumen(long AdjuntoId, string Tipo, string? NombreArchivo, string Estado);

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
    IReadOnlyList<PostulacionResumen> Postulaciones,
    /// <summary>
    /// COR-11: el desenlace que fija la columna, o nulo si no es final. La pantalla lo usa para
    /// pedir confirmacion al soltar en «Descartado», sin depender del nombre de la columna.
    /// </summary>
    string? EstadoResultante = null);

/// <summary>
/// Nombres de estado que viajan como texto en <see cref="ConversacionResumen.Estado"/>. El Frontend
/// solo conoce Contracts (no el enum de Domain), y compararlos con literales sueltos por pantalla es
/// lo que hace que un renombre rompa una vista y nadie se entere.
/// </summary>
public static class EstadosConversacion
{
    /// <summary>Bandeja general: el bot no pudo clasificar el hilo y espera que un analista lo tome (FUN-01).</summary>
    public const string PendienteClasificar = "PendienteClasificar";

    public const string EnMenuBot = "EnMenuBot";
    public const string Activa = "Activa";
    public const string Archivada = "Archivada";
}

/// <summary>
/// Nombres de desenlace que viajan como texto en <see cref="PostulacionResumen.Estado"/>. Misma razon
/// que <see cref="EstadosConversacion"/>: el Frontend no conoce el enum de Domain.
/// </summary>
public static class EstadosPostulacion
{
    public const string EnProceso = "EnProceso";
    public const string Contratado = "Contratado";
    public const string Descartado = "Descartado";
    public const string Archivada = "Archivada";

    /// <summary>A2: la persona vuelve a un proceso. Cuenta como vivo (FUN-08).</summary>
    public const string Reingreso = "Reingreso";
}

/// <summary>
/// Nombres de estado que viajan como texto en <see cref="MensajeResumen.EstadoEntrega"/>. Misma razon
/// que <see cref="EstadosConversacion"/>.
/// </summary>
public static class EstadosEntrega
{
    /// <summary>Decidido y guardado, todavia sin salir (V29).</summary>
    public const string EnCola = "EnCola";

    public const string Enviando = "Enviando";
    public const string Pendiente = "Pendiente";
    public const string Enviado = "Enviado";
    public const string Entregado = "Entregado";
    public const string Leido = "Leido";
    public const string Fallido = "Fallido";
}

/// <summary>
/// Nombres de estado que viajan como texto en <see cref="AdjuntoResumen.Estado"/>. Misma razon que
/// <see cref="EstadosConversacion"/>.
/// </summary>
public static class EstadosAdjunto
{
    /// <summary>Registrado, todavia sin bajar ni escanear.</summary>
    public const string Pendiente = "Pendiente";

    /// <summary>Paso el antivirus y se puede bajar.</summary>
    public const string Descargado = "Descargado";

    /// <summary>No paso el antivirus, el tope o el tipo, o no se pudo bajar.</summary>
    public const string Rechazado = "Rechazado";

    /// <summary>Borrado por la retencion de la Regla 17.</summary>
    public const string Purgado = "Purgado";
}
