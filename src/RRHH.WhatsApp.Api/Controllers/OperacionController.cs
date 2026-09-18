using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RRHH.WhatsApp.Api.Seguridad;
using RRHH.WhatsApp.Contracts.Administracion;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Api.Controllers;

/// <summary>
/// FUN-15 (M1) — lo que el sistema no puede resolver solo: una plantilla que Meta no aprobo, una
/// vacante abierta sin formulario, un menu sin opciones.
/// <para>
/// Sin esta pantalla, <c>AlertasOperativas</c> es una tabla donde los problemas se guardan y nadie los
/// ve: el bot deja de mandar el recordatorio y el aviso queda ahi, esperando a que alguien mire la
/// base. Jefatura ve lo que hay que arreglar; resolverlo es de Sistemas, que es quien lo arregla y
/// quien responde por darlo por hecho (V23).
/// </para>
/// </summary>
[ApiController]
[Route("operacion")]
[Authorize(Policy = Politicas.Jefatura)]
public sealed class OperacionController(
    IAlertaOperativaService alertas,
    ILogger<OperacionController> log) : ControllerBase
{
    /// <summary>Las abiertas, agrupadas por tipo y clave con su contador de ocurrencias.</summary>
    [HttpGet("alertas")]
    public async Task<IActionResult> Listar(CancellationToken ct)
    {
        var abiertas = await alertas.ListarAbiertasAsync(ct);

        return Ok(abiertas
            .Select(a => new AlertaOperativaResumen(
                a.AlertaId, a.Tipo, a.Clave, a.Detalle, a.Ocurrencias, a.FechaPrimera, a.FechaUltima))
            .ToList());
    }

    /// <summary>
    /// Darla por arreglada. No cierra el problema: si vuelve a pasar se abre una alerta nueva, y eso es
    /// lo que hace visible que algo se dio por resuelto sin estarlo (V32).
    /// </summary>
    [HttpPost("alertas/{id:int}/resolver")]
    [Authorize(Policy = Politicas.Estructura)]
    public async Task<IActionResult> Resolver(int id, CancellationToken ct)
    {
        if (!await alertas.ResolverAsync(id, User.AnalistaId(), ct))
            return NotFound(new { motivo = "La alerta no existe o ya estaba resuelta." });

        log.LogInformation("Alerta {AlertaId} resuelta por el analista {AnalistaId}.", id, User.AnalistaId());

        return NoContent();
    }
}
