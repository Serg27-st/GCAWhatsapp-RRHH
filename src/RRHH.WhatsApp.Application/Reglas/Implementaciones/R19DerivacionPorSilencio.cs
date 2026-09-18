using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas.Implementaciones;

/// <summary>
/// Regla 19 — derivacion por silencio (FUN-06, A12).
/// <para>
/// El postulante escribio algo que el bot no entendio y despues se callo. El menu queda ahi, pero
/// nadie lo esta atendiendo: el bot no puede avanzar solo y ningun analista ve el hilo. Pasado el
/// plazo, se deriva a «Sin clasificar», que es donde alguien puede tomarlo (P3).
/// </para>
/// <para>
/// En horas habiles: escribir a las once de la noche no tiene por que derivar a la una de la
/// mañana, cuando nadie lo va a tomar igual.
/// </para>
/// </summary>
public sealed class R19DerivacionPorSilencio : IReglaNegocio
{
    public string Codigo => "R19";

    public string Descripcion => "Deriva a «Sin clasificar» el hilo que quedo en silencio tras un texto no reconocido.";

    /// <summary>Antes del escalamiento: el hilo todavia no tiene analista al que escalarle nada.</summary>
    public int Prioridad => 23;

    public bool Aplica(ContextoRegla ctx) =>
        ctx.Disparador == TipoDisparador.TiempoTranscurrido
        && ctx.Conversacion is { Estado: EstadoConversacion.EnMenuBot, FechaTextoNoReconocido: not null };

    public Task<ResultadoRegla> EvaluarAsync(ContextoRegla ctx, CancellationToken ct = default)
    {
        var horas = ctx.ConfigInt(ClavesConfiguracion.MenuHorasDerivacion, 2);

        if (ctx.MinutosHabilesDesdeTextoNoReconocido is not { } minutos || minutos < horas * 60)
            return Task.FromResult(ResultadoRegla.SinAccion);

        return Task.FromResult(ResultadoRegla.Detener(
            new DerivarAPendientes(
                $"El postulante no volvio a escribir tras {minutos:F0} minutos habiles desde un texto no reconocido.")));
    }
}
