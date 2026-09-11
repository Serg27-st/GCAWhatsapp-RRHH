using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas.Implementaciones;

/// <summary>
/// Regla 9 — seguimiento del JobForms.
/// <para>
/// Quien recibio el link y no completo el formulario recibe un recordatorio a las 24 horas; si aun
/// asi no lo completa, a las 48 se le avisa al analista. Los dos plazos viven en
/// <see cref="ClavesConfiguracion"/>, y el barrido del Worker es lo que la dispara: junto con la
/// Regla 2 y la 16, es de las que responden al paso del tiempo y no a un mensaje.
/// </para>
/// </summary>
public sealed class R09SeguimientoJobForms : IReglaNegocio
{
    public string Codigo => "R09";

    public string Descripcion => "Recuerda el JobForms a las 24h y avisa al analista a las 48h.";

    /// <summary>Despues del archivado y el escalamiento, que deciden el destino del hilo.</summary>
    public int Prioridad => 40;

    public bool Aplica(ContextoRegla ctx) =>
        ctx.Disparador == TipoDisparador.TiempoTranscurrido
        && ctx.Conversacion is not null
        && ctx.Invitacion is { Completado: false };

    public Task<ResultadoRegla> EvaluarAsync(ContextoRegla ctx, CancellationToken ct = default)
    {
        var invitacion = ctx.Invitacion!;
        var horas = (ctx.AhoraUtc - invitacion.FechaEnvioLink).TotalHours;

        var horasRecordatorio = ctx.ConfigInt(ClavesConfiguracion.RecordatorioJobFormsHoras, 24);
        var horasAviso = ctx.ConfigInt(ClavesConfiguracion.AvisoAnalistaJobFormsHoras, 48);

        var acciones = new List<AccionRegla>();

        if (!invitacion.RecordatorioEnviado && horas >= horasRecordatorio)
        {
            acciones.Add(new EnviarPlantilla(
                ClavesPlantilla.RecordatorioJobForms,
                [
                    ctx.Postulante?.NombreCompleto ?? "hola",
                    invitacion.Hc?.Titulo ?? "la vacante",
                    ctx.EnlaceInvitacion ?? string.Empty
                ]));

            // Se sella aunque el envio termine omitido por falta de plantilla aprobada. Reintentar
            // en cada barrido convertiria un problema de configuracion en una tanda de mensajes
            // repetidos, que es exactamente lo que eleva los reportes de spam (Seccion 2.4).
            acciones.Add(new MarcarRecordatorioJobForms(invitacion.InvitacionId));
        }

        // Si el hilo todavia no tiene analista no hay a quien avisarle: se deja pendiente para
        // cuando la Regla 1 o la 14 lo asignen.
        if (!invitacion.AvisoAnalistaEnviado
            && horas >= horasAviso
            && ctx.Conversacion!.AnalistaAtendiendoId is { } analistaId)
        {
            var vacante = invitacion.Hc?.Titulo ?? "la vacante";

            acciones.Add(new NotificarAnalista(
                analistaId,
                $"El postulante no completo el formulario de {vacante} tras {horasAviso} horas."));

            acciones.Add(new MarcarAvisoAnalistaJobForms(invitacion.InvitacionId));

            acciones.Add(new RegistrarAuditoria(
                "AvisoJobFormsIncompleto",
                $"Invitacion {invitacion.InvitacionId} sin completar tras {horas:F0} horas."));
        }

        return Task.FromResult(acciones.Count == 0
            ? ResultadoRegla.SinAccion
            : ResultadoRegla.Con([.. acciones]));
    }
}
