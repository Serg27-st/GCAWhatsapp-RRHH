using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas;

/// <summary>
/// Arma el <see cref="ContextoRegla"/> leyendo todo lo que las reglas necesitan de una sola vez.
/// <para>
/// Existe para que ninguna regla consulte la base por su cuenta: esa es la condicion que hace que
/// cada una se pueda probar aislada, con un contexto armado a mano (Seccion 9.6.6).
/// </para>
/// <para>
/// Todos reciben la <c>claveEjecucion</c> (V29): la pone quien conoce la identidad del disparador
/// —el id del evento, la conversacion y el minuto del barrido— para que reevaluarlo produzca las
/// mismas claves de envio y la cola no duplique.
/// </para>
/// </summary>
public interface IFabricaContextoRegla
{
    /// <summary>
    /// Contexto para un mensaje que acaba de llegar por el webhook.
    /// <para>
    /// <paramref name="fechaActividadAnterior"/> es la instantanea que tomo el webhook antes de registrar
    /// el mensaje (ARQ-07): despues de registrarlo ya no se puede reconstruir.
    /// </para>
    /// </summary>
    Task<ContextoRegla> ParaMensajeEntranteAsync(
        int conversacionId, long mensajeId, string? idBotonPulsado, DateTime? fechaActividadAnterior,
        Guid correlationId, string claveEjecucion, CancellationToken ct = default);

    /// <summary>Contexto para el barrido del Worker: escalamientos, recordatorios y archivado.</summary>
    Task<ContextoRegla> ParaTiempoTranscurridoAsync(
        int conversacionId, string claveEjecucion, CancellationToken ct = default);


    /// <summary>
    /// Contexto para el barrido por postulacion: cierre de cortesia, aviso de archivado y archivado
    /// (FUN-10, FUN-11). Va por postulacion y no por conversacion porque lo que vence es el proceso:
    /// una persona puede tener uno archivandose en una cuenta y otro vivo en otra.
    /// </summary>
    Task<ContextoRegla> ParaTiempoPostulacionAsync(
        int postulacionId, string claveEjecucion, CancellationToken ct = default);
    /// <summary>Contexto para cuando el postulante completo el JobForms (Regla 9).</summary>
    Task<ContextoRegla> ParaJobFormsCompletadoAsync(
        int conversacionId, int hcId, Guid correlationId, string claveEjecucion, CancellationToken ct = default);

    /// <summary>Contexto para cuando el analista mueve o marca una postulacion (Reglas 7, 12 y 13).</summary>
    Task<ContextoRegla> ParaCambioEstadoPostulacionAsync(
        int postulacionId, Guid correlationId, string claveEjecucion, CancellationToken ct = default);

    /// <summary>Contexto para validar un envio antes de que salga (Regla 15).</summary>
    Task<ContextoRegla> ParaEnvioSalienteAsync(
        int conversacionId, Guid correlationId, string claveEjecucion, CancellationToken ct = default);
}
