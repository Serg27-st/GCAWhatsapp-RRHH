using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas.Implementaciones;

/// <summary>
/// Regla 19 — plazo de «Sin clasificar» (FUN-06, P3).
/// <para>
/// La bandeja general la ven todos, que es como decir que no la mira nadie. Si un hilo lleva ahi mas
/// del plazo configurado sin que ningun analista lo tome, se avisa a Jefatura para que alguien lo
/// asigne: ningun hilo sin dueño ni plazo.
/// </para>
/// </summary>
public sealed class R19AvisoPendiente : IReglaNegocio
{
    public string Codigo => "R19";

    public string Descripcion => "Avisa a Jefatura cuando un hilo lleva demasiado tiempo en «Sin clasificar».";

    /// <summary>Junto a la derivacion que lo puso ahi, pero despues: el plazo empieza a correr recien entonces.</summary>
    public int Prioridad => 24;

    public bool Aplica(ContextoRegla ctx) =>
        ctx.Disparador == TipoDisparador.TiempoTranscurrido
        && ctx.Conversacion is
        {
            Estado: EstadoConversacion.PendienteClasificar,
            FechaAvisoPendiente: null,
            FechaPendienteDesde: not null
        };

    public Task<ResultadoRegla> EvaluarAsync(ContextoRegla ctx, CancellationToken ct = default)
    {
        var horas = ctx.ConfigInt(ClavesConfiguracion.ClasificacionHorasAviso, 2);

        if (ctx.MinutosHabilesEnPendiente is not { } minutos || minutos < horas * 60)
            return Task.FromResult(ResultadoRegla.SinAccion);

        return Task.FromResult(ResultadoRegla.Con(
            new NotificarRol(
                RolAnalista.Jefatura,
                $"Hay un postulante sin clasificar hace {minutos / 60:F0} h habiles y nadie lo tomo."),
            new SellarConversacion(MarcaConversacion.AvisoPendiente),
            new RegistrarAuditoria(
                "AvisoSinClasificar",
                $"Sin tomar tras {minutos:F0} minutos habiles en la bandeja general.")));
    }
}
