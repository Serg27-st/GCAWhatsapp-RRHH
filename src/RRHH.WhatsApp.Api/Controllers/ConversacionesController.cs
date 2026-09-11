using RRHH.WhatsApp.Api.Seguridad;
using Microsoft.AspNetCore.Mvc;
using RRHH.WhatsApp.Api.Mapeo;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Contracts.Bandeja;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Api.Controllers;

/// <summary>
/// La bandeja del analista (Seccion 7). Todo lo que expone es interno: no hay login todavia, asi
/// que el `analistaId` viaja como parametro y estos endpoints deben quedar detras de la
/// autenticacion antes de salir a produccion (Seccion 9.6.1).
/// </summary>
[ApiController]
[Route("conversaciones")]
public sealed class ConversacionesController(
    IConversacionService conversaciones,
    IMensajeService mensajes,
    IPostulacionService postulaciones,
    EnvioAnalista envio,
    AccionesBandeja acciones,
    ILogger<ConversacionesController> log) : ControllerBase
{
    /// <summary>
    /// Regla 4: el analista ve solo lo suyo; el rol Sistemas ve todo, para soporte y auditoria.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct)
    {
        try
        {
            var hilos = await conversaciones.ListarParaAnalistaAsync(User.AnalistaId(), ct);

            return Ok(hilos.Select(c => c.AResumen()));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { motivo = ex.Message });
        }
    }

    /// <summary>Regla 19: bandeja general de pendientes por clasificar, visible para todos.</summary>
    [HttpGet("pendientes")]
    public async Task<IActionResult> Pendientes(CancellationToken ct)
    {
        var hilos = await conversaciones.ListarPendientesClasificarAsync(ct);

        return Ok(hilos.Select(c => c.AResumen()));
    }

    /// <summary>Buscador por DNI de la Seccion 7: trae directamente el chat del postulante.</summary>
    [HttpGet("buscar")]
    public async Task<IActionResult> Buscar([FromQuery] string dni, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dni))
            return BadRequest(new { motivo = "Falta el DNI." });

        var hilos = await conversaciones.BuscarPorDniAsync(dni.Trim(), ct);

        return Ok(hilos.Select(c => c.AResumen()));
    }

    /// <summary>El chat completo, con el aviso multi-cuenta de la Regla 6 en la cabecera.</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detalle(int id, CancellationToken ct)
    {
        var conversacion = await conversaciones.ObtenerPorIdAsync(id, ct);

        if (conversacion is null)
            return NotFound();

        var hilo = await mensajes.ListarPorConversacionAsync(id, ct: ct);

        // Regla 6: aviso generico de que el mismo DNI esta en proceso con otras cuentas, sin
        // dejar ver el detalle de esas conversaciones.
        var otrasCuentas = conversacion.PostulanteId is { } postulanteId && conversacion.CuentaContextoId is { } cuentaId
            ? (await postulaciones.ObtenerOtrasCuentasEnProcesoAsync(postulanteId, cuentaId, ct))
                .Select(c => c.Nombre).ToList()
            : [];

        var propias = conversacion.PostulanteId is { } id2
            ? (await postulaciones.ObtenerTableroPorPostulanteAsync(id2, ct)).Select(p => p.AResumen()).ToList()
            : [];

        return Ok(new ConversacionDetalle(
            conversacion.AResumen(otrasCuentas),
            [.. hilo.Select(m => m.AResumen())],
            propias));
    }

    /// <summary>Regla 15: es aca donde se aplica el limite de opt-in y la ventana de 24h.</summary>
    [HttpPost("{id:int}/responder")]
    public async Task<IActionResult> Responder(
        int id, [FromBody] PeticionResponder peticion, CancellationToken ct)
    {
        try
        {
            var resultado = await envio.ResponderAsync(
                id, User.AnalistaId(), peticion.Texto, peticion.ClavePlantilla, peticion.Parametros, ct);

            var respuesta = new ResultadoResponder(
                resultado.Enviado, resultado.Motivo, resultado.RequierePlantilla, resultado.MensajeId);

            // 422 y no 400: la peticion es valida, lo que no corresponde es el envio.
            return resultado.Enviado ? Ok(respuesta) : UnprocessableEntity(respuesta);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { motivo = ex.Message });
        }
    }

    /// <summary>Regla 8: transferencia manual a otro analista, de uno en uno.</summary>
    [HttpPost("{id:int}/transferir")]
    public async Task<IActionResult> Transferir(
        int id, [FromBody] PeticionTransferir peticion, CancellationToken ct)
    {
        try
        {
            var transferencia = await conversaciones.TransferirAsync(
                id, User.AnalistaId(), peticion.AnalistaDestinoId,
                peticion.Urgente, peticion.Comentario, ct);

            return Ok(new
            {
                transferencia.TransferenciaId,
                estado = transferencia.Estado.ToString(),
                transferencia.Urgente
            });
        }
        catch (InvalidOperationException ex)
        {
            log.LogWarning(ex, "Transferencia rechazada en la conversacion {ConversacionId}.", id);
            return UnprocessableEntity(new { motivo = ex.Message });
        }
    }

    /// <summary>Regla 7: whitelist (motivo opcional) o blacklist (motivo obligatorio).</summary>
    [HttpPost("{id:int}/marcar")]
    public async Task<IActionResult> Marcar(
        int id, [FromBody] PeticionMarcar peticion, CancellationToken ct)
    {
        if (!Enum.TryParse<TipoEstadoPostulante>(peticion.Tipo, ignoreCase: true, out var tipo))
            return BadRequest(new { motivo = $"Tipo '{peticion.Tipo}' desconocido. Use Whitelist o Blacklist." });

        try
        {
            await acciones.MarcarAsync(
                peticion.PostulanteId, peticion.CuentaId, tipo, peticion.Motivo, User.AnalistaId(), ct);

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { motivo = ex.Message });
        }
    }

    /// <summary>Regla 13: mueve la postulacion entre columnas del tablero.</summary>
    [HttpPost("{id:int}/etapa")]
    public async Task<IActionResult> MoverEtapa(
        int id, [FromBody] PeticionMoverEtapa peticion, CancellationToken ct)
    {
        try
        {
            await acciones.MoverEtapaAsync(peticion.PostulacionId, peticion.EtapaId, User.AnalistaId(), ct);

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { motivo = ex.Message });
        }
    }
}
