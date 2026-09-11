using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Application.Casos;

/// <summary>Lo que queda creado tras un envio valido del formulario.</summary>
public sealed record ResultadoJobForms(int PostulanteId, int PostulacionId, int RespuestaId);

/// <summary>
/// Cierra el circuito del JobForms: toma el envio del formulario, crea a la persona y su
/// postulacion, y devuelve la conversacion al flujo de WhatsApp.
/// <para>
/// El aviso al postulante no sale de aca sino de la outbox, por la misma razon que en el webhook
/// de WhatsApp: quien nos llama —Google Apps Script hoy, el formulario propio manana— espera una
/// respuesta rapida, y evaluar reglas y salir a la red no cabe en ese tiempo.
/// </para>
/// </summary>
public sealed class RecepcionJobForms(
    IJobFormsInvitacionService invitaciones,
    IJobFormsService formularios,
    IPostulanteService postulantes,
    IPostulacionService postulaciones,
    IConversacionService conversaciones,
    IEventoSistemaService eventos,
    ILogger<RecepcionJobForms> log)
{
    public async Task<ResultadoJobForms> ProcesarAsync(EnvioJobForms envio, CancellationToken ct = default)
    {
        var invitacion = await invitaciones.ObtenerPorTokenAsync(envio.Token, ct)
            ?? throw new InvalidOperationException("El enlace del formulario no corresponde a ninguna invitacion.");

        // Regla 9: el DNI es el identificador, no el telefono. La misma persona que ya postulo
        // antes se reconoce aca y no se duplica.
        var postulante = await postulantes.RegistrarDesdeFormularioAsync(envio.Postulante, ct);

        var respuesta = new JobFormsRespuesta
        {
            InvitacionId = invitacion.InvitacionId,
            PostulanteId = postulante.PostulanteId,
            HcId = invitacion.HcId,
            DatosJson = envio.DatosJson,
            CvUrl = envio.CvUrl,
            ConsentimientoAceptado = envio.ConsentimientoAceptado
        };

        // Valida vacante abierta (Regla 20) y consentimiento (Regla 17) antes de guardar nada.
        // Si alguna falla, lanza y no queda ni la respuesta ni la postulacion a medio crear.
        await formularios.ValidarEnvioAsync(respuesta, ct);

        var postulacion = await postulaciones.CrearAsync(postulante.PostulanteId, invitacion.HcId, ct);

        // Recien aca el hilo de WhatsApp sabe a quien pertenece: hasta ahora solo tenia un telefono.
        await conversaciones.VincularPostulanteAsync(invitacion.ConversacionId, postulante.PostulanteId, ct);

        await invitaciones.MarcarCompletadoAsync(invitacion.InvitacionId, ct);

        var correlationId = Guid.NewGuid();

        await eventos.PublicarAsync(TiposEvento.JobFormsCompletado, new
        {
            invitacion.ConversacionId,
            invitacion.HcId,
            postulante.PostulanteId,
            postulacion.PostulacionId,
            respuesta.RespuestaId
        }, correlationId, ct);

        log.LogInformation(
            "JobForms recibido: postulante {PostulanteId}, vacante {HcId}, conversacion {ConversacionId}.",
            postulante.PostulanteId, invitacion.HcId, invitacion.ConversacionId);

        return new ResultadoJobForms(postulante.PostulanteId, postulacion.PostulacionId, respuesta.RespuestaId);
    }
}
