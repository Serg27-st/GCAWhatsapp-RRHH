using Microsoft.AspNetCore.Mvc;
using RRHH.WhatsApp.Contracts.Bandeja;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Api.Controllers;

/// <summary>
/// Administracion del analista: sus cuentas y sus ausencias planificadas. Las dos cosas alimentan
/// el enrutamiento, asi que dejarlas solo en SQL Server obligaria a tocar la base a mano.
/// </summary>
[ApiController]
[Route("analistas")]
public sealed class AnalistasController(
    ICuentaService cuentas,
    IAusenciaService ausencias,
    IAnalistaService analistas) : ControllerBase
{
    /// <summary>
    /// Cuentas asignadas y en cuales es respaldo. Es lo que la bandeja usa para agrupar por
    /// subdivision (Seccion 7).
    /// </summary>
    [HttpGet("{id:int}/cuentas")]
    public async Task<IActionResult> Cuentas(int id, CancellationToken ct)
    {
        var asignadas = await cuentas.ListarDeAnalistaAsync(id, ct);

        return Ok(asignadas.Select(a =>
            new CuentaDeAnalista(a.Cuenta.CuentaId, a.Cuenta.Nombre, a.EsBackup, a.VacantesAbiertas)));
    }

    /// <summary>
    /// Catalogo de analistas activos. La bandeja lo usa para ofrecer destinos de transferencia
    /// (Regla 8); mientras no haya login, tambien para elegir con que analista se trabaja.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct)
    {
        var activos = await analistas.ListarActivosAsync(ct);

        return Ok(activos.Select(a => new AnalistaResumen(a.AnalistaId, a.Nombre, a.Email, a.Rol.ToString())));
    }

    /// <summary>
    /// Alta de analista. El rol Sistemas ve todas las conversaciones (Regla 4); el resto solo las
    /// de sus cuentas.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] PeticionCrearAnalista peticion, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(peticion.Nombre) || string.IsNullOrWhiteSpace(peticion.Email))
            return BadRequest(new { motivo = "El analista necesita nombre y email." });

        if (!Enum.TryParse<RolAnalista>(peticion.Rol, ignoreCase: true, out var rol))
            return BadRequest(new { motivo = $"Rol '{peticion.Rol}' desconocido. Use Analista o Sistemas." });

        try
        {
            var analista = await analistas.CrearAsync(peticion.Nombre, peticion.Email, rol, ct);

            return CreatedAtAction(nameof(Listar), new AnalistaResumen(
                analista.AnalistaId, analista.Nombre, analista.Email, analista.Rol.ToString()));
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { motivo = ex.Message });
        }
    }

    /// <summary>
    /// Regla 14: mientras dure la ausencia, las conversaciones nuevas de sus cuentas van directo
    /// al respaldo, sin esperar las 2 horas de la Regla 2.
    /// </summary>
    [HttpPost("{id:int}/ausencias")]
    public async Task<IActionResult> RegistrarAusencia(
        int id, [FromBody] PeticionAusencia peticion, CancellationToken ct)
    {
        if (peticion.FechaFin <= peticion.FechaInicio)
            return BadRequest(new { motivo = "La ausencia termina antes de empezar." });

        var ausencia = await ausencias.RegistrarAsync(
            id, peticion.FechaInicio, peticion.FechaFin, peticion.Motivo, ct);

        return Ok(new { ausencia.AusenciaId, ausencia.AnalistaId, ausencia.FechaInicio, ausencia.FechaFin });
    }

    /// <summary>
    /// Si el analista esta ausente ahora mismo. La bandeja lo usa para no ofrecerlo como destino
    /// de una transferencia que nadie va a atender (Regla 8 sobre Regla 14).
    /// </summary>
    [HttpGet("{id:int}/ausente")]
    public async Task<IActionResult> Ausente(int id, CancellationToken ct) =>
        Ok(new { analistaId = id, ausente = await ausencias.EstaAusenteAsync(id, DateTime.UtcNow, ct) });
}
