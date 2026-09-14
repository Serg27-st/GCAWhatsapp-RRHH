using Microsoft.AspNetCore.Authorization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Api.Configuracion;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Contracts.Bandeja;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Almacenamiento;

namespace RRHH.WhatsApp.Api.Controllers;

/// <summary>
/// Circuito publico del formulario de postulacion. Es la unica superficie del sistema alcanzable
/// desde internet sin autenticacion, asi que todo lo que entra por aca pasa antes por el limite de
/// velocidad y por la comprobacion del secreto compartido (Seccion 9.6.1).
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("jobforms")]
[EnableRateLimiting(PoliticasLimite.Publico)]
public sealed class JobFormsController(
    RecepcionJobForms recepcion,
    IJobFormsInvitacionService invitaciones,
    IJobFormsService formularios,
    ICuentaService cuentas,
    IOptions<OpcionesJobForms> opciones,
    IOptions<OpcionesCv> opcionesCv,
    ILogger<JobFormsController> log) : ControllerBase
{
    /// <summary>
    /// Tope duro del request, por encima del limite configurable de <see cref="OpcionesCv"/>.
    /// <para>
    /// El atributo del framework necesita una constante, asi que no puede leer la configuracion.
    /// Se deja holgado a proposito: corta un cuerpo desmedido antes de que ASP.NET lo lea entero,
    /// y el limite real —el que se ajusta sin recompilar— lo aplica el codigo mas abajo.
    /// </para>
    /// </summary>
    private const long TopeDuroBytes = 50 * 1024 * 1024;

    public const string CabeceraSecreto = "X-JobForms-Secreto";

    private readonly OpcionesJobForms _opciones = opciones.Value;
    private readonly OpcionesCv _cv = opcionesCv.Value;

    /// <summary>
    /// Estado del enlace antes de que el postulante llene nada. Regla 20: si la vacante ya se
    /// cubrio, el formulario no debe siquiera abrirse.
    /// </summary>
    [HttpGet("{token:guid}")]
    public async Task<IActionResult> Estado(Guid token, CancellationToken ct)
    {
        var invitacion = await invitaciones.ObtenerPorTokenAsync(token, ct);

        if (invitacion is null)
            return NotFound(new { motivo = "El enlace no corresponde a ninguna postulacion." });

        if (invitacion.Completado)
            return Ok(new { vigente = false, motivo = "Este formulario ya fue enviado." });

        if (!await formularios.ValidarVacanteActivaAsync(invitacion.HcId, ct))
            return Ok(new { vigente = false, motivo = "La vacante ya fue cubierta." });

        // Los campos opcionales que el analista configuro para esta vacante. Van aca y no en el
        // envio porque el formulario los necesita antes de pintarse, para saber que preguntar
        // ademas de los campos fijos (Seccion 9.2).
        var campos = await cuentas.ListarCamposOpcionalesAsync(invitacion.HcId, ct);

        return Ok(new
        {
            vigente = true,
            vacante = invitacion.Hc?.Titulo,
            invitacion.HcId,
            camposOpcionales = campos
                .Where(c => c.Activo)
                .Select(c => new CampoOpcional(c.NombreCampo, c.Tipo.ToString(), c.Activo))
        });
    }

    /// <summary>
    /// Lo que llama el Apps Script de Google cuando alguien envia el formulario. Es el camino
    /// activo mientras el JobForms viva en Google Forms.
    /// </summary>
    [HttpPost("webhook-google")]
    public async Task<IActionResult> WebhookGoogle(
        [FromBody] EnvioGoogleForms cuerpo, CancellationToken ct)
    {
        if (!SecretoValido())
            return Unauthorized();

        // Sin token valido no hay forma de saber de que postulacion se trata. Es un rechazo
        // del negocio, no un error del servidor: el enlace se armo mal o alguien entro sin el.
        if (!cuerpo.TokenValido(out var token))
            return UnprocessableEntity(new { motivo = "El enlace del formulario no trae un token valido." });

        var envio = new EnvioJobForms(
            token,
            new DatosPostulanteFormulario(
                cuerpo.Dni.Trim(), cuerpo.NombreCompleto, cuerpo.TelefonoE164, cuerpo.Email),
            cuerpo.DatosJson ?? "{}",
            // Google guarda el adjunto en Drive: lo que llega es su enlace, no el archivo. La
            // consecuencia esta anotada en docs/decisiones.md: la purga de la Regla 17 puede
            // limpiar la referencia pero no borrar el archivo en el origen.
            cuerpo.CvUrl,
            cuerpo.ConsentimientoAceptado);

        return await ProcesarAsync(envio, ct);
    }

    /// <summary>
    /// Camino propio, para cuando el formulario se migre a Razor Pages. Recibe el CV como archivo
    /// y lo guarda en el almacenamiento de la empresa, que es lo que permite cumplir de verdad la
    /// retencion de la Regla 17.
    /// </summary>
    [HttpPost("{token:guid}/enviar")]
    [RequestSizeLimit(TopeDuroBytes)]
    public async Task<IActionResult> Enviar(
        Guid token,
        [FromForm] EnvioPropio cuerpo,
        IFormFile? cv,
        CancellationToken ct)
    {
        string? rutaCv = null;

        if (cv is { Length: > 0 })
        {
            // Se corta antes de tocar el disco. El almacenamiento vuelve a contar mientras copia
            // —ese es el control real, porque el largo lo declara quien sube el archivo— pero
            // rechazar aca evita escribir y borrar 10 MB para terminar en el mismo error.
            var tope = (long)_cv.TamanoMaximoMb * 1024 * 1024;

            if (cv.Length > tope)
            {
                return StatusCode(
                    StatusCodes.Status413PayloadTooLarge,
                    new { motivo = $"El CV supera el maximo de {_cv.TamanoMaximoMb} MB permitido." });
            }

            await using var contenido = cv.OpenReadStream();

            rutaCv = await formularios.AlmacenarCvAsync(
                contenido, cv.FileName, cv.ContentType, ct);
        }

        var envio = new EnvioJobForms(
            token,
            new DatosPostulanteFormulario(
                cuerpo.Dni.Trim(), cuerpo.NombreCompleto, cuerpo.TelefonoE164, cuerpo.Email),
            cuerpo.DatosJson ?? "{}",
            rutaCv,
            cuerpo.ConsentimientoAceptado);

        return await ProcesarAsync(envio, ct);
    }

    private async Task<IActionResult> ProcesarAsync(EnvioJobForms envio, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(envio.Postulante.Dni))
            return BadRequest(new { motivo = "El DNI es obligatorio." });

        try
        {
            var resultado = await recepcion.ProcesarAsync(envio, ct);

            // YaRecibido le dice al Apps Script que su reintento llego a destino y que no hay nada
            // que volver a mandar (V27).
            return Ok(new { resultado.PostulanteId, resultado.PostulacionId, resultado.YaRecibido });
        }
        catch (InvalidOperationException ex)
        {
            // Vacante cerrada, consentimiento faltante o token desconocido. Son respuestas
            // esperables del negocio, no fallas del servidor: el formulario tiene que poder
            // mostrarle al postulante que paso.
            log.LogWarning(ex, "Envio de JobForms rechazado.");

            return UnprocessableEntity(new { motivo = ex.Message });
        }
    }

    /// <summary>Comparacion de tiempo fijo: una comparacion normal filtra el secreto por el reloj.</summary>
    private bool SecretoValido()
    {
        if (!_opciones.EstaConfigurado)
        {
            log.LogError("No hay secreto configurado para el webhook de JobForms. Se rechaza el envio.");
            return false;
        }

        if (!Request.Headers.TryGetValue(CabeceraSecreto, out var recibido))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(recibido.ToString()),
            Encoding.UTF8.GetBytes(_opciones.SecretoWebhook));
    }

    /// <summary>Lo que manda el Apps Script del formulario de Google.</summary>
    public sealed record EnvioGoogleForms(
        string Token,
        string Dni,
        string? NombreCompleto,
        string? TelefonoE164,
        string? Email,
        string? DatosJson,
        string? CvUrl,
        bool ConsentimientoAceptado)
    {
        /// <summary>
        /// El token llega tal como viajo en el enlace. El sistema lo arma sin guiones y Google lo
        /// devuelve igual, pero System.Text.Json solo entiende la forma con guiones: por eso se
        /// recibe como texto y se interpreta aca, aceptando las dos (V27).
        /// </summary>
        public bool TokenValido(out Guid token) => Guid.TryParse(Token, out token);
    }

    /// <summary>Misma estructura, para el formulario propio. El CV viaja aparte como archivo.</summary>
    public sealed record EnvioPropio(
        string Dni,
        string? NombreCompleto,
        string? TelefonoE164,
        string? Email,
        string? DatosJson,
        bool ConsentimientoAceptado);
}
