using Microsoft.AspNetCore.Mvc;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Api.Controllers;

/// <summary>
/// Consultas sobre la persona detras de las postulaciones, y el borrado efectivo que exige la
/// Regla 17. No es un endpoint publico: vive detras de la autenticacion de la bandeja.
/// </summary>
[ApiController]
[Route("postulantes")]
public sealed class PostulantesController(
    IPostulanteService postulantes,
    ILogger<PostulantesController> log) : ControllerBase
{
    /// <summary>
    /// Historial completo a traves de todas las cuentas. El flujo lo pide para mostrarselo al
    /// analista cuando el DNI ya existe (Seccion 6.1).
    /// </summary>
    [HttpGet("{dni}/historial")]
    public async Task<IActionResult> Historial(string dni, CancellationToken ct)
    {
        var postulante = await postulantes.BuscarPorDniAsync(dni, ct);

        if (postulante is null)
            return NotFound();

        var historial = await postulantes.ObtenerHistorialAsync(dni, ct);

        return Ok(new
        {
            postulante.PostulanteId,
            postulante.Dni,
            postulante.NombreCompleto,
            postulaciones = historial.Select(p => new
            {
                p.PostulacionId,
                cuenta = p.Cuenta?.Nombre,
                vacante = p.Hc?.Titulo,
                etapa = p.EtapaKanban?.Nombre,
                estado = p.Estado.ToString(),
                p.FechaCreacion
            })
        });
    }

    /// <summary>
    /// Regla 17: solicitud de eliminacion de datos personales. Anonimiza en vez de borrar filas,
    /// para no llevarse por delante las metricas historicas de la Regla 18.
    /// </summary>
    [HttpDelete("{dni}")]
    public async Task<IActionResult> Eliminar(
        string dni, [FromQuery] string? motivo, CancellationToken ct)
    {
        try
        {
            await postulantes.AnonimizarDatosAsync(
                dni, motivo ?? "Solicitud del titular de los datos.", ct);

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            log.LogWarning(ex, "No se pudo anonimizar el DNI solicitado.");
            return NotFound(new { motivo = ex.Message });
        }
    }
}
