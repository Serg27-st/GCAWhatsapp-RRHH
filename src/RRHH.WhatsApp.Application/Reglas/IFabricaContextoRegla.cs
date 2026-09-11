using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas;

/// <summary>
/// Arma el <see cref="ContextoRegla"/> leyendo todo lo que las reglas necesitan de una sola vez.
/// <para>
/// Existe para que ninguna regla consulte la base por su cuenta: esa es la condicion que hace que
/// cada una se pueda probar aislada, con un contexto armado a mano (Seccion 9.6.6).
/// </para>
/// </summary>
public interface IFabricaContextoRegla
{
    /// <summary>Contexto para un mensaje que acaba de llegar por el webhook.</summary>
    Task<ContextoRegla> ParaMensajeEntranteAsync(
        int conversacionId, string? idBotonPulsado, Guid correlationId, CancellationToken ct = default);

    /// <summary>Contexto para el barrido del Worker: escalamientos, recordatorios y archivado.</summary>
    Task<ContextoRegla> ParaTiempoTranscurridoAsync(int conversacionId, CancellationToken ct = default);

    /// <summary>Contexto para cuando el postulante completo el JobForms (Regla 9).</summary>
    Task<ContextoRegla> ParaJobFormsCompletadoAsync(
        int conversacionId, int hcId, Guid correlationId, CancellationToken ct = default);

    /// <summary>Contexto para cuando el analista mueve o marca una postulacion (Reglas 7, 12 y 13).</summary>
    Task<ContextoRegla> ParaCambioEstadoPostulacionAsync(
        int postulacionId, Guid correlationId, CancellationToken ct = default);

    /// <summary>Contexto para validar un envio antes de que salga (Regla 15).</summary>
    Task<ContextoRegla> ParaEnvioSalienteAsync(
        int conversacionId, Guid correlationId, CancellationToken ct = default);
}
