using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas.Implementaciones;

/// <summary>
/// Regla 9 — confirmacion tras completar el formulario.
/// <para>
/// El flujo pide que el sistema valide el envio y devuelva al postulante al chat con un mensaje de
/// confirmacion; desde ahi la conversacion queda disponible para el analista de la cuenta. Es
/// tambien el momento en que se sella el opt-in por la via del formulario (Regla 15).
/// </para>
/// </summary>
public sealed class R09ConfirmacionJobForms : IReglaNegocio
{
    public string Codigo => "R09";

    public string Descripcion => "Confirma por WhatsApp que el formulario se recibio.";

    /// <summary>Despues de que la Regla 1 o la 14 resolvieron a que analista queda el hilo.</summary>
    public int Prioridad => 46;

    public bool Aplica(ContextoRegla ctx) =>
        ctx.Disparador == TipoDisparador.JobFormsCompletado
        && ctx.Conversacion is not null;

    public Task<ResultadoRegla> EvaluarAsync(ContextoRegla ctx, CancellationToken ct = default)
    {
        var vacante = ctx.Hc?.Titulo ?? ctx.Invitacion?.Hc?.Titulo ?? "la vacante";
        var nombre = ctx.Postulante?.NombreCompleto;

        return Task.FromResult(ResultadoRegla.Con(
            // Completar el formulario es consentimiento por derecho propio (Regla 15). Si ya
            // estaba sellado por el mensaje entrante, esto no lo mueve.
            new RegistrarOptIn(OrigenOptIn.JobFormsCompletado),
            // COR-03 (P1): dentro de la ventana sale en texto. Con EnviarPlantilla el postulante no
            // recibia nada, porque la plantilla sigue esperando la aprobacion de Meta (C3).
            new EnviarMensajeBot(
                TextosBot.ConfirmacionFormulario(nombre, vacante),
                ClavesPlantilla.ConfirmacionJobForms,
                [nombre ?? "hola", vacante]),
            new RegistrarAuditoria(
                "JobFormsCompletado",
                $"Formulario recibido para {vacante}.")));
    }
}
