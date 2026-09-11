using Microsoft.AspNetCore.Mvc;
using RRHH.WhatsApp.Reporting;

namespace RRHH.WhatsApp.Api.Controllers;

/// <summary>
/// Regla 18 — panel de gerencia. Lee del modelo de solo lectura y nunca de los servicios de
/// dominio: un reporte pesado no debe competir con las conversaciones que se estan atendiendo.
/// </summary>
[ApiController]
[Route("reportes")]
public sealed class ReportesController(IReportingReadModel reporting) : ControllerBase
{
    /// <summary>Dias que cubre el panel cuando no se indica periodo. Un mes es el ciclo del negocio.</summary>
    private const int DiasPorDefecto = 30;

    [HttpGet("metricas")]
    public async Task<IActionResult> Metricas(
        [FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, CancellationToken ct)
    {
        var hastaUtc = hasta?.ToUniversalTime() ?? DateTime.UtcNow;
        var desdeUtc = desde?.ToUniversalTime() ?? hastaUtc.AddDays(-DiasPorDefecto);

        if (hastaUtc <= desdeUtc)
            return BadRequest(new { motivo = "El periodo termina antes de empezar." });

        return Ok(await reporting.ObtenerMetricasAsync(desdeUtc, hastaUtc, ct));
    }
}
