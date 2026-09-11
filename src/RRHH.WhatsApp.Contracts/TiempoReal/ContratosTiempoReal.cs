namespace RRHH.WhatsApp.Contracts.TiempoReal;

/// <summary>
/// Aviso en vivo para un analista (Sección 9.6.3). Lo produce el motor de reglas —asignación
/// multi-cuenta, escalamiento, transferencia recibida, formulario sin completar— y llega a la
/// bandeja sin que nadie refresque.
/// </summary>
public sealed record NotificacionAnalista(
    int AnalistaId,
    int? ConversacionId,
    string Mensaje,
    DateTime FechaUtc);

/// <summary>
/// Nombres del hub y sus métodos. Constantes compartidas para que el servidor y el cliente no se
/// separen por una cadena mal escrita: una diferencia acá no falla al compilar, falla en silencio
/// en producción.
/// </summary>
public static class CanalBandeja
{
    public const string Ruta = "/hub/bandeja";

    /// <summary>El cliente lo llama para empezar a recibir sus avisos. El analista sale del token.</summary>
    public const string Suscribir = nameof(Suscribir);

    /// <summary>El servidor lo invoca en el cliente al llegar un aviso.</summary>
    public const string RecibirNotificacion = nameof(RecibirNotificacion);
}
