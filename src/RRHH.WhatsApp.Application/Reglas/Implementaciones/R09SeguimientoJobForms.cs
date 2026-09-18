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
            var nombre = ctx.Postulante?.NombreCompleto;
            var vacante = invitacion.Hc?.Titulo ?? "la vacante";
            var enlace = ctx.EnlaceInvitacion ?? string.Empty;

            // A las 24h de silencio la ventana suele estar cerrada, asi que este suele salir como
            // plantilla; el texto vale cuando el postulante escribio algo mientras tanto (COR-03).
            acciones.Add(new EnviarMensajeBot(
                TextosBot.RecordatorioFormulario(nombre, vacante, enlace),
                ClavesPlantilla.RecordatorioJobForms,
                [nombre ?? "hola", vacante, enlace]));

            // COR-03: solo se sella si el recordatorio llego a encolarse. Sellarlo igual —como se hacia
            // antes— lo perdia para siempre: el barrido lo daba por enviado y el postulante nunca lo
            // recibia. Sin sello y sin envio, la alerta agrupada pide aprobar la plantilla (V32).
            acciones.Add(new MarcarRecordatorioJobForms(invitacion.InvitacionId) { SoloSiSeEnvioAnterior = true });
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
