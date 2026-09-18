using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RRHH.WhatsApp.Api.Seguridad;
using RRHH.WhatsApp.Contracts.Bandeja;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Api.Controllers;

/// <summary>
/// Alta de clientes y su dotacion. Sin estas rutas el sistema arranca pero no puede enrutar nada:
/// el menu del bot no tiene empresas que ofrecer y la Regla 1 no tiene a quien asignarle el hilo.
/// <para>
/// Ver la dotacion es para todos: la bandeja la necesita para saber a quien transferir (Regla 8).
/// Dar de alta una cuenta es de Sistemas; decidir quien la cubre, de Jefatura (V23).
/// </para>
/// </summary>
[ApiController]
[Route("cuentas")]
public sealed class CuentasController(ICuentaService cuentas, ILogger<CuentasController> log) : ControllerBase
{
    /// <summary>Cada cuenta con su titular y su respaldo: es como se ve si la operacion esta lista.</summary>
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct)
    {
        var filas = await cuentas.ListarConDotacionAsync(ct);

        return Ok(filas.Select(f => new CuentaDetalle(
            f.Cuenta.CuentaId,
            f.Cuenta.Nombre,
            f.Cuenta.Activo,
            f.Titular is null ? null : new AnalistaResumen(
                f.Titular.AnalistaId, f.Titular.Nombre, f.Titular.Email, f.Titular.Rol.ToString()),
            f.Respaldo is null ? null : new AnalistaResumen(
                f.Respaldo.AnalistaId, f.Respaldo.Nombre, f.Respaldo.Email, f.Respaldo.Rol.ToString()),
            f.VacantesAbiertas)));
    }

    [HttpPost]
    [Authorize(Policy = Politicas.Estructura)]
    public async Task<IActionResult> Crear([FromBody] PeticionCrearCuenta peticion, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(peticion.Nombre))
            return BadRequest(new { motivo = "La cuenta necesita un nombre." });

        try
        {
            var cuenta = await cuentas.CrearAsync(peticion.Nombre, ct);

            return CreatedAtAction(nameof(Listar), new { cuenta.CuentaId, cuenta.Nombre });
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { motivo = ex.Message });
        }
    }

    /// <summary>
    /// Regla 1 y Regla 2: pone al analista como titular o como respaldo. Cada rol es uno solo por
    /// cuenta, asi que asignar reemplaza a quien lo ocupaba.
    /// </summary>
    /// <summary>
    /// FUN-20: corregir el nombre o desactivar la cuenta. Desactivarla la saca del menú del bot, pero no
    /// mueve lo que ya está en curso: si quedan conversaciones abiertas queda una alerta operativa.
    /// </summary>
    [HttpPatch("{id:int}")]
    [Authorize(Policy = Politicas.Estructura)]
    public async Task<IActionResult> Editar(
        int id, [FromBody] PeticionEditarCuenta peticion, CancellationToken ct)
    {
        try
        {
            await cuentas.ActualizarCuentaAsync(id, peticion.Nombre, peticion.Activo, User.AnalistaId(), ct);

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { motivo = ex.Message });
        }
    }

    [HttpPost("{id:int}/analistas")]
    [Authorize(Policy = Politicas.Jefatura)]
    public async Task<IActionResult> Asignar(
        int id, [FromBody] PeticionAsignarAnalista peticion, CancellationToken ct)
    {
        try
        {
            await cuentas.AsignarAnalistaAsync(id, peticion.AnalistaId, peticion.EsBackup, ct);

            log.LogInformation(
                "El analista {Autor} asigno a {AnalistaId} como {Rol} de la cuenta {CuentaId}.",
                User.AnalistaId(), peticion.AnalistaId, peticion.EsBackup ? "respaldo" : "titular", id);

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            log.LogWarning(ex, "No se pudo asignar el analista a la cuenta {CuentaId}.", id);

            return UnprocessableEntity(new { motivo = ex.Message });
        }
    }

    [HttpDelete("{id:int}/analistas/{analistaId:int}")]
    [Authorize(Policy = Politicas.Jefatura)]
    public async Task<IActionResult> Quitar(int id, int analistaId, CancellationToken ct)
    {
        await cuentas.QuitarAnalistaAsync(id, analistaId, ct);

        return NoContent();
    }
}
