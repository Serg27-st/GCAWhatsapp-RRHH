using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas.Implementaciones;

/// <summary>
/// Regla 19 — Fallback de menu no reconocido.
/// <para>
/// Si el postulante escribe texto libre en vez de usar los botones, el bot reintenta una vez
/// mostrando el menu. Si sigue sin elegir una opcion valida, la conversacion pasa a la bandeja
/// general de pendientes por clasificar, visible para todos los analistas, para que cualquiera
/// la tome manualmente.
/// </para>
/// </summary>
public sealed class R19FallbackMenu : IReglaNegocio
{
    /// <summary>Un solo reintento antes de derivar a la bandeja general, como define la regla.</summary>
    private const int ReintentosPermitidos = 1;

    public string Codigo => "R19";

    public string Descripcion => "Reintenta el menu una vez y luego deriva a la bandeja general de pendientes por clasificar.";

    /// <summary>Antes que la asignacion: mientras no haya cuenta identificada no hay a quien asignar.</summary>
    public int Prioridad => 15;

    public bool Aplica(ContextoRegla ctx) =>
        ctx.Disparador == TipoDisparador.MensajeEntrante
        && ctx.Conversacion is not null
        // Solo interviene mientras el bot no logro identificar la cuenta.
        && ctx.Cuenta is null;

    public Task<ResultadoRegla> EvaluarAsync(ContextoRegla ctx, CancellationToken ct = default)
    {
        if (ctx.IntentosMenuFallidos <= ReintentosPermitidos)
        {
            return Task.FromResult(ResultadoRegla.Con(
                new MostrarMenuEmpresas(EsReintento: ctx.IntentosMenuFallidos > 0)));
        }

        // Agotado el reintento, la conversacion no se pierde: queda visible para todo el equipo.
        return Task.FromResult(ResultadoRegla.Con(
            new CambiarEstadoConversacion(EstadoConversacion.PendienteClasificar),
            new RegistrarAuditoria(
                "DerivadaABandejaGeneral",
                $"El postulante no eligio una opcion valida tras {ctx.IntentosMenuFallidos} intentos.")));
    }
}
