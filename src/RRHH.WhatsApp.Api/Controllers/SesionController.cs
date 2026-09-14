using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RRHH.WhatsApp.Api.Configuracion;
using RRHH.WhatsApp.Api.Seguridad;
using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Contracts.Seguridad;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Api.Controllers;

/// <summary>
/// Inicio de sesión de los analistas (Sección 9.6.1).
/// <para>
/// Va detrás del mismo límite de velocidad que los endpoints públicos: sin eso, probar
/// contraseñas contra este endpoint es gratis.
/// </para>
/// </summary>
[ApiController]
[Route("sesion")]
[EnableRateLimiting(PoliticasLimite.Publico)]
public sealed class SesionController(
    IAutenticacionService autenticacion,
    IAnalistaService analistas,
    EmisorTokens emisor,
    IOptions<OpcionesArranque> arranque,
    ILogger<SesionController> log) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] PeticionLogin peticion, CancellationToken ct)
    {
        var resultado = await autenticacion.VerificarAsync(
            peticion.Email?.Trim() ?? string.Empty, peticion.Contrasena ?? string.Empty, ct);

        if (!resultado.Exito)
            return Unauthorized(new { motivo = resultado.Motivo });

        var (token, expira) = emisor.Emitir(resultado);

        return Ok(new SesionIniciada(
            token, expira, resultado.AnalistaId, resultado.Nombre!, resultado.Rol!));
    }

    /// <summary>Quién soy, según el token. La bandeja lo usa para saber si la sesión sigue viva.</summary>
    [HttpGet("yo")]
    [Authorize]
    public IActionResult Yo() => Ok(new
    {
        analistaId = User.AnalistaId(),
        nombre = User.Identity?.Name,
        esSistemas = User.EsSistemas()
    });

    /// <summary>Cambiar la propia contraseña. Exige la actual: un token robado no debe poder fijar otra.</summary>
    [HttpPut("contrasena")]
    [Authorize]
    public async Task<IActionResult> CambiarContrasena(
        [FromBody] PeticionCambiarContrasena peticion, CancellationToken ct)
    {
        var yo = await analistas.ObtenerPorIdAsync(User.AnalistaId(), ct);

        if (yo is null)
            return Unauthorized();

        var verificacion = await autenticacion.VerificarAsync(yo.Email, peticion.ContrasenaActual, ct);

        if (!verificacion.Exito)
            return UnprocessableEntity(new { motivo = "La contraseña actual no es correcta." });

        try
        {
            await autenticacion.EstablecerContrasenaAsync(yo.AnalistaId, peticion.ContrasenaNueva, ct);

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { motivo = ex.Message });
        }
    }

    /// <summary>
    /// Restablecer la contraseña de otro analista. Solo el rol Sistemas, que es el que tiene
    /// visibilidad y soporte según la Regla 4.
    /// </summary>
    [HttpPut("analistas/{id:int}/contrasena")]
    [Authorize(Roles = ClaimsAnalista.RolSistemas)]
    public async Task<IActionResult> Restablecer(
        int id, [FromBody] PeticionRestablecerContrasena peticion, CancellationToken ct)
    {
        try
        {
            await autenticacion.EstablecerContrasenaAsync(id, peticion.Contrasena, ct);

            log.LogInformation(
                "El analista {Autor} restablecio la contrasena de {Objetivo}.", User.AnalistaId(), id);

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { motivo = ex.Message });
        }
    }

    /// <summary>
    /// Alta de la primera contraseña, para poder entrar por primera vez tras el despliegue.
    /// <para>
    /// Solo funciona mientras NINGÚN analista tenga contraseña. En cuanto existe la primera, esta
    /// puerta se cierra sola y para siempre — no hay forma de volver a abrirla sin vaciar la
    /// columna en la base, que ya es acceso de administrador.
    /// </para>
    /// <para>
    /// Es solo para Sistemas (V25): es el unico rol que despues puede restablecer las contraseñas de
    /// los demas, asi que una primera contraseña de otro rol cerraria la puerta y dejaria a todos
    /// afuera. En una base nueva todavia no hay analistas: si el correo es el configurado en
    /// <c>Arranque:EmailSistemas</c>, se lo da de alta aca con rol Sistemas.
    /// </para>
    /// </summary>
    [HttpPost("arranque")]
    [AllowAnonymous]
    public async Task<IActionResult> Arranque([FromBody] PeticionArranque peticion, CancellationToken ct)
    {
        if (!await autenticacion.SinContrasenasAsync(ct))
            return Conflict(new { motivo = "Ya hay contraseñas configuradas. Use el restablecimiento." });

        var email = peticion.Email?.Trim() ?? string.Empty;

        // El alta guarda el correo en minusculas: compararlo tal cual lo escribio quien arranca dejaria
        // afuera a "Sistemas@Empresa.pe" sin decir por que.
        var analista = (await analistas.ListarActivosAsync(ct))
            .FirstOrDefault(a => string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase));

        if (analista is not null && analista.Rol != RolAnalista.Sistemas)
        {
            return UnprocessableEntity(new
            {
                motivo = $"El arranque es para un analista de Sistemas, y {analista.Email} tiene rol {analista.Rol}: " +
                         "solo Sistemas puede despues restablecer las contraseñas de los demás."
            });
        }

        // Sin analista con ese correo —tipicamente, una base nueva— solo se crea el de Sistemas que se
        // configuro en el servidor. La Api recibe el webhook de Meta y es alcanzable desde internet:
        // sin esa condicion, cualquiera que llegara primero a un despliegue recien publicado se daria
        // de alta con visibilidad total.
        if (analista is null && !arranque.Value.Admite(email))
            return NotFound(new { motivo = "No hay un analista activo con ese correo." });

        try
        {
            if (analista is null)
            {
                analista = await analistas.CrearAsync(
                    string.IsNullOrWhiteSpace(peticion.Nombre) ? "Sistemas" : peticion.Nombre,
                    email, RolAnalista.Sistemas, ct);

                log.LogWarning(
                    "Arranque: se dio de alta el analista de Sistemas {Email}, que no existia.", analista.Email);
            }

            // Si la contraseña no sirve, el analista recien creado queda sin ella y la puerta sigue
            // abierta: reintentar con una valida lo completa.
            await autenticacion.EstablecerContrasenaAsync(analista.AnalistaId, peticion.Contrasena, ct);

            log.LogWarning(
                "Arranque: se fijo la primera contrasena del sistema para {Email}.", analista.Email);

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { motivo = ex.Message });
        }
    }
}
