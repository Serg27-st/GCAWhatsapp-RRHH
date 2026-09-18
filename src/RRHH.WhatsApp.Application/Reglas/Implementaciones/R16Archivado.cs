using RRHH.WhatsApp.Domain.Calendario;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas.Implementaciones;

/// <summary>
/// Regla 16 — archivado de la postulacion por inactividad (FUN-11, A14, COR-14).
/// <para>
/// Lo que vence por silencio es el proceso, no el hilo: una persona puede tener una postulacion
/// dormida en una cuenta y otra viva en otra. Por eso corre en el barrido por postulacion, y el
/// archivado del hilo lo decide aparte <see cref="R16ArchivadoConversacion"/>.
/// </para>
/// <para>
/// Antes de archivar una en curso, el analista recibe un aviso con la fecha: es cuando todavia puede
/// hacer algo. Contratado y Reingreso no se archivan por silencio: el dossier los excluye de forma
/// explicita (A2).
/// </para>
/// </summary>
public sealed class R16Archivado : IReglaNegocio
{
    public string Codigo => "R16";

    public string Descripcion => "Avisa y luego archiva la postulacion que lleva los dias configurados sin actividad.";

    /// <summary>Despues del cierre de cortesia: la despedida se decide con la postulacion todavia descartada.</summary>
    public int Prioridad => 62;

    public bool Aplica(ContextoRegla ctx) =>
        ctx.Disparador == TipoDisparador.TiempoTranscurridoPostulacion
        && ctx.Postulacion is { Estado: EstadoPostulacion.EnProceso or EstadoPostulacion.Descartado };

    public Task<ResultadoRegla> EvaluarAsync(ContextoRegla ctx, CancellationToken ct = default)
    {
        var postulacion = ctx.Postulacion!;

        var diasLimite = ctx.ConfigInt(ClavesConfiguracion.ArchivadoDias, 90);
        var diasAviso = ctx.ConfigInt(ClavesConfiguracion.ArchivadoAvisoDias, 7);
        var diasSinActividad = (ctx.AhoraUtc - postulacion.FechaUltimaActividad).TotalDays;

        if (diasSinActividad >= diasLimite)
        {
            return Task.FromResult(ResultadoRegla.Con(
                new ArchivarPostulacion(
                    postulacion.PostulacionId,
                    $"Sin actividad durante {diasSinActividad:F0} dias.")));
        }

        // A14: el aviso es para quien todavia puede rescatar el proceso. Una descartada ya no lo
        // necesita: se archiva sin molestar a nadie.
        if (postulacion.Estado != EstadoPostulacion.EnProceso
            || postulacion.FechaAvisoArchivado is not null
            || diasSinActividad < diasLimite - diasAviso)
        {
            return Task.FromResult(ResultadoRegla.SinAccion);
        }

        // Sin analista asignado, el aviso va al titular de la cuenta. Sin ninguno, no se sella: el
        // aviso sale en cuanto alguien quede a cargo.
        if ((postulacion.AnalistaAsignadoId ?? ctx.AnalistaTitular?.AnalistaId) is not { } analistaId)
            return Task.FromResult(ResultadoRegla.SinAccion);

        var fecha = ZonaHorariaPeru.ALocal(postulacion.FechaUltimaActividad.AddDays(diasLimite));
        var vacante = postulacion.Hc?.Titulo ?? ctx.Hc?.Titulo ?? "una vacante";

        return Task.FromResult(ResultadoRegla.Con(
            new NotificarAnalista(
                analistaId,
                $"La postulacion de {ctx.Postulante?.NombreCompleto ?? "un postulante"} a {vacante} " +
                $"se archivara el {fecha:dd/MM/yyyy} si no tiene movimiento."),
            new SellarPostulacion(postulacion.PostulacionId, MarcaPostulacion.AvisoArchivado)));
    }
}
