using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas.Implementaciones;

/// <summary>
/// Regla 16 — cierre definitivo del hilo (FUN-11).
/// <para>
/// Un hilo sin actividad durante los dias configurados, y sin ningun proceso que lo sostenga, se
/// archiva para que la bandeja no crezca indefinidamente. Es independiente de la repregunta de la
/// Regla 9: aquella reabre la conversacion, esta la cierra.
/// </para>
/// <para>
/// Un proceso en curso, un reingreso o una contratacion mantienen el hilo abierto: el dossier archiva
/// solo lo que no tiene marca de contratado o reingreso, y lo que sigue en curso lo archiva primero
/// <see cref="R16Archivado"/> por postulacion.
/// </para>
/// </summary>
public sealed class R16ArchivadoConversacion : IReglaNegocio
{
    public string Codigo => "R16";

    public string Descripcion => "Archiva el hilo tras los dias configurados sin actividad y sin procesos vigentes.";

    /// <summary>
    /// Antes del escalamiento: si el hilo ya se va a archivar, escalarlo al respaldo solo le
    /// entrega a alguien una conversacion muerta.
    /// </summary>
    public int Prioridad => 22;

    public bool Aplica(ContextoRegla ctx) =>
        ctx.Disparador == TipoDisparador.TiempoTranscurrido
        && ctx.Conversacion is not null
        && ctx.Conversacion.Estado != EstadoConversacion.Archivada;

    public Task<ResultadoRegla> EvaluarAsync(ContextoRegla ctx, CancellationToken ct = default)
    {
        var conversacion = ctx.Conversacion!;

        var diasLimite = ctx.ConfigInt(ClavesConfiguracion.ArchivadoDias, 90);
        var diasSinActividad = (ctx.AhoraUtc - conversacion.FechaUltimaActividad).TotalDays;

        if (diasSinActividad < diasLimite)
            return Task.FromResult(ResultadoRegla.SinAccion);

        var vigente = ctx.TieneProcesoVivo
            || ctx.PostulacionesDelPostulante.Any(p => p.Estado == EstadoPostulacion.Contratado);

        if (vigente)
            return Task.FromResult(ResultadoRegla.SinAccion);

        // ARQ-07: el archivado es un cambio de estado mas su rastro, no una accion aparte. Detiene la
        // evaluacion: lo que sigue son reglas que actuarian sobre un hilo que acaba de cerrarse.
        return Task.FromResult(ResultadoRegla.Detener(
            new CambiarEstadoConversacion(EstadoConversacion.Archivada),
            new RegistrarAuditoria("Archivado", $"Sin actividad durante {diasSinActividad:F0} dias.")));
    }
}
