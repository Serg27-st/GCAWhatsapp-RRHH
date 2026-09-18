using RRHH.WhatsApp.Api.Seguridad;
using Microsoft.AspNetCore.Mvc;
using RRHH.WhatsApp.Contracts.Bandeja;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Api.Controllers;

/// <summary>
/// Regla 8: el analista destino acepta o rechaza la transferencia que recibio.
/// <para>
/// Vive aparte de <c>ConversacionesController</c> porque la respuesta la da el destinatario y no
/// el dueno del hilo: la ruta es de la transferencia, no de la conversacion.
/// </para>
/// </summary>
[ApiController]
[Route("transferencias")]
public sealed class TransferenciasController(
    IConversacionService conversaciones,
    ILogger<TransferenciasController> log) : ControllerBase
{
    /// <summary>
    /// Lo que espera respuesta de quien está autenticado. Es la otra mitad de la Regla 8: sin esta
    /// lista, el aviso de "te transfirieron algo" no lleva a ninguna parte.
    /// </summary>
    [HttpGet("pendientes")]
    public async Task<IActionResult> Pendientes(CancellationToken ct)
    {
        var pendientes = await conversaciones.ListarTransferenciasPendientesAsync(User.AnalistaId(), ct);

        return Ok(pendientes.Select(t => new TransferenciaPendiente(
            t.TransferenciaId,
            t.ConversacionId,
            t.AnalistaOrigen?.Nombre ?? $"Analista {t.AnalistaOrigenId}",
            t.Conversacion?.CuentaContexto?.Nombre,
            t.Conversacion?.Postulante?.NombreCompleto,
            t.Conversacion?.TelefonoE164,
            t.Comentario,
            t.Fecha,
            t.FechaVencimiento)));
    }


    /// <summary>
    /// FUN-07: quien la envió la retira mientras nadie la haya respondido. El destino se entera por
    /// el mismo canal que cuando se la ofrecieron.
    /// </summary>
    [HttpPost("{id:int}/retirar")]
    public async Task<IActionResult> Retirar(int id, CancellationToken ct)
    {
        try
        {
            await conversaciones.RetirarTransferenciaAsync(id, User.AnalistaId(), ct);

            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { motivo = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // Retirar una ajena o una ya respondida: ninguno se arregla reintentando.
            log.LogWarning(ex, "Retiro rechazado para la transferencia {TransferenciaId}.", id);

            return UnprocessableEntity(new { motivo = ex.Message });
        }
    }

    /// <summary>Lo que quien está autenticado ofreció y todavía espera respuesta (FUN-07).</summary>
    [HttpGet("enviadas")]
    public async Task<IActionResult> Enviadas(CancellationToken ct)
    {
        var enviadas = await conversaciones.ListarTransferenciasEnviadasPendientesAsync(User.AnalistaId(), ct);

        return Ok(enviadas.Select(t => new TransferenciaEnviada(
            t.TransferenciaId,
            t.ConversacionId,
            t.AnalistaDestino?.Nombre ?? $"Analista {t.AnalistaDestinoId}",
            t.Conversacion?.Postulante?.NombreCompleto,
            t.Fecha,
            t.FechaVencimiento)));
    }
    [HttpPost("{id:int}/responder")]
    public async Task<IActionResult> Responder(
        int id, [FromBody] PeticionResponderTransferencia peticion, CancellationToken ct)
    {
        try
        {
            await conversaciones.ResponderTransferenciaAsync(id, User.AnalistaId(), peticion.Aceptada, ct);

            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            // Un id inexistente es 404, no 422: no hay nada que el analista pueda corregir en el
            // cuerpo de la peticion.
            return NotFound(new { motivo = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // Cubre los dos rechazos legitimos: responder una transferencia ajena y responder una
            // que ya fue contestada. Ninguno se arregla reintentando, asi que no es un 500.
            log.LogWarning(ex, "Respuesta rechazada para la transferencia {TransferenciaId}.", id);

            return UnprocessableEntity(new { motivo = ex.Message });
        }
    }
}
