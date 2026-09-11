using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas.Implementaciones;

/// <summary>
/// Regla 16 — Cierre definitivo del caso.
/// <para>
/// Un hilo sin actividad durante los dias configurados se archiva, para que la base de postulantes
/// activos no crezca indefinidamente. Es independiente de la repregunta por inactividad de la
/// Regla 9: aquella reabre la conversacion, esta la cierra.
/// </para>
/// </summary>
public sealed class R16Archivado : IReglaNegocio
{
    public string Codigo => "R16";

    public string Descripcion => "Archiva el caso tras los dias configurados sin actividad.";

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

        // Un proceso vivo o uno que termino en contratacion no se archiva por silencio. El
        // reingreso que menciona el dossier todavia no tiene donde registrarse en el modelo
        // (ver docs/decisiones.md), asi que aca solo pesan los dos estados que si existen.
        var vigente = ctx.EstadosPostulaciones.Any(
            e => e is EstadoPostulacion.EnProceso or EstadoPostulacion.Contratado);

        if (vigente)
            return Task.FromResult(ResultadoRegla.SinAccion);

        // Detiene la evaluacion: lo que sigue son reglas que actuarian sobre un hilo que acaba
        // de cerrarse.
        return Task.FromResult(ResultadoRegla.Detener(
            new ArchivarConversacion($"Sin actividad durante {diasSinActividad:F0} dias.")));
    }
}
