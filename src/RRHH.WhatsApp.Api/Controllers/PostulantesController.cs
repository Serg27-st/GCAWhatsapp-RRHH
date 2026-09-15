using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RRHH.WhatsApp.Api.Seguridad;
using RRHH.WhatsApp.Domain.Entidades;
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
    ICuentaService cuentas,
    ILogger<PostulantesController> log) : ControllerBase
{
    /// <summary>
    /// Historial previo del postulante, que el flujo pide mostrarle al analista cuando el DNI ya
    /// existe (Seccion 6.1).
    /// <para>
    /// Es el de las cuentas de quien pregunta. Lo que la persona hizo en otras cuentas es de otros
    /// analistas, y de eso la bandeja ya da el aviso generico de la Regla 6. Sistemas lo ve completo.
    /// </para>
    /// </summary>
    [HttpGet("{dni}/historial")]
    public async Task<IActionResult> Historial(string dni, CancellationToken ct)
    {
        // T0.07a: se busca con la misma forma con la que el JobForms guarda el DNI.
        if (!DocumentoIdentidad.TryNormalizar(dni, out var normalizado))
            return BadRequest(new { motivo = "El DNI no tiene un formato valido." });

        dni = normalizado;

        var postulante = await postulantes.BuscarPorDniAsync(dni, ct);

        if (postulante is null)
            return NotFound();

        var historial = await postulantes.ObtenerHistorialAsync(dni, ct);

        if (!User.EsSistemas())
        {
            var propias = (await cuentas.ListarDeAnalistaAsync(User.AnalistaId(), ct))
                .Select(c => c.Cuenta.CuentaId)
                .ToHashSet();

            historial = [.. historial.Where(p => propias.Contains(p.CuentaId))];

            // Sin nada en sus cuentas, la persona no es asunto de este analista: devolver su
            // nombre confirmaria que ese DNI esta en proceso en otra cuenta.
            if (historial.Count == 0)
                return NotFound();
        }

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
    /// <para>
    /// Solo Sistemas: anonimiza a la persona en todas las cuentas, y no puede decidirlo un analista
    /// que por la Regla 4 ve solo la suya.
    /// </para>
    /// </summary>
    [HttpDelete("{dni}")]
    [Authorize(Roles = ClaimsAnalista.RolSistemas)]
    public async Task<IActionResult> Eliminar(
        string dni, [FromQuery] string? motivo, CancellationToken ct)
    {
        // T0.07a: con el DNI escrito de otra forma la anonimizacion responderia 404 y la solicitud
        // de la Regla 17 quedaria sin cumplir, con los datos todavia en la base.
        if (!DocumentoIdentidad.TryNormalizar(dni, out var normalizado))
            return BadRequest(new { motivo = "El DNI no tiene un formato valido." });

        try
        {
            await postulantes.AnonimizarDatosAsync(
                normalizado, motivo ?? "Solicitud del titular de los datos.", ct);

            log.LogInformation("El analista {AnalistaId} anonimizo los datos de un postulante.", User.AnalistaId());

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            log.LogWarning(ex, "No se pudo anonimizar el DNI solicitado.");
            return NotFound(new { motivo = ex.Message });
        }
    }
}
