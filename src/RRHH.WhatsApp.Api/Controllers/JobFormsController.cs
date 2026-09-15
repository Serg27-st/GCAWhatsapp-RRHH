using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Api.Configuracion;
using RRHH.WhatsApp.Api.Seguridad;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Contracts.Bandeja;
using RRHH.WhatsApp.Domain.Entidades;
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

    /// <summary>Alias del nombre real de la cabecera: <see cref="SecretoJobForms.Cabecera"/>, compartido con el limitador de velocidad (COR-15).</summary>
    public const string CabeceraSecreto = SecretoJobForms.Cabecera;

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
    /// <remarks>
    /// Su propio limite de velocidad (COR-15), particionado por secreto valido y no por IP:
    /// reemplaza al de la clase para esta accion. <see cref="Estado"/> y <see cref="Enviar"/> siguen
    /// con <see cref="PoliticasLimite.Publico"/>.
    /// </remarks>
    [HttpPost("webhook-google")]
    [EnableRateLimiting(PoliticasLimite.WebhookGoogle)]
    public async Task<IActionResult> WebhookGoogle(
        [FromBody] EnvioGoogleForms cuerpo, CancellationToken ct)
    {
        if (!SecretoValido())
            return Unauthorized();

        // Sin token valido no hay forma de saber de que postulacion se trata. Es un rechazo
        // del negocio, no un error del servidor: el enlace se armo mal o alguien entro sin el.
        if (!cuerpo.TokenValido(out var token))
            return UnprocessableEntity(new { motivo = "El enlace del formulario no trae un token valido." });

        // COR-15/M8: antes esto era "cuerpo.Dni.Trim()", que tumbaba el webhook con un 500 si el
        // DNI llegaba nulo. El DNI es dato personal (Regla 17), asi que el motivo no lo repite.
        if (!DocumentoIdentidad.TryNormalizar(cuerpo.Dni, out var dni))
            return UnprocessableEntity(new { motivo = "El DNI no tiene un formato valido." });

        // COR-15/M8: el analista termina abriendo este enlace a ciegas. Sin dominio permitido, se
        // rechaza antes de guardar nada.
        if (!ValidadorCvUrl.EsValida(cuerpo.CvUrl, _opciones.DominiosCvPermitidosEfectivos))
            return UnprocessableEntity(new { motivo = "El enlace del CV no es valido." });

        var envio = new EnvioJobForms(
            token,
            new DatosPostulanteFormulario(dni, cuerpo.NombreCompleto, cuerpo.TelefonoE164, cuerpo.Email),
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
        // COR-15/M8: el DNI invalido o nulo se rechaza antes de mirar la invitacion, la vacante o
        // el disco -- es lo mas barato de comprobar y evita el mismo 500 que tumbaba el webhook.
        if (!DocumentoIdentidad.TryNormalizar(cuerpo.Dni, out var dni))
            return UnprocessableEntity(new { motivo = "El DNI no tiene un formato valido." });

        // Sin invitacion no hay a que postulacion atribuir el envio, igual que en el webhook de
        // Google: es el mismo tipo de rechazo del negocio, no un 404 de recurso HTTP.
        var invitacion = await invitaciones.ObtenerPorTokenAsync(token, ct);

        if (invitacion is null)
        {
            return UnprocessableEntity(
                new { motivo = "El enlace del formulario no corresponde a ninguna invitacion." });
        }

        string? rutaCv = null;

        // V27: una invitacion ya completada es un reintento, no un envio nuevo. No se revalida la
        // vacante -- pudo cerrarse desde el primer envio, y eso no debe convertir un reintento en
        // un rechazo -- ni se guarda un CV: recepcion.ProcesarAsync va a responder YaRecibido sin
        // tocar el disco, que es la promesa de la invariante 9 (todo CV con fila, y viceversa).
        if (!invitacion.Completado)
        {
            // Regla 20: la vacante pudo cerrarse mientras el postulante llenaba el formulario. Se
            // revalida aca, antes de tocar el disco, ademas de la revalidacion que hace
            // recepcion.ProcesarAsync mas abajo -- es la unica forma de no guardar el CV de una
            // vacante que ya no admite postulaciones.
            if (!await formularios.ValidarVacanteActivaAsync(invitacion.HcId, ct))
            {
                return UnprocessableEntity(
                    new { motivo = $"La vacante {invitacion.HcId} ya no admite postulaciones." });
            }

            if (cv is { Length: > 0 })
            {
                // Se corta antes de tocar el disco. El almacenamiento vuelve a contar mientras
                // copia —ese es el control real, porque el largo lo declara quien sube el
                // archivo— pero rechazar aca evita escribir y borrar 10 MB para terminar en el
                // mismo error.
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
        }

        var envio = new EnvioJobForms(
            token,
            new DatosPostulanteFormulario(dni, cuerpo.NombreCompleto, cuerpo.TelefonoE164, cuerpo.Email),
            cuerpo.DatosJson ?? "{}",
            rutaCv,
            cuerpo.ConsentimientoAceptado);

        return await ProcesarAsync(envio, ct, rutaCv);
    }

    /// <summary>
    /// Comun a los dos caminos. <paramref name="rutaCvParaLimpiar"/> solo lo trae <see cref="Enviar"/>:
    /// es el CV que ya quedo escrito en disco antes de saber si <c>recepcion.ProcesarAsync</c> lo
    /// iba a aceptar. El webhook de Google no guarda archivo propio —<c>CvUrl</c> es una referencia
    /// a Drive—, asi que no tiene nada que limpiar.
    /// </summary>
    private async Task<IActionResult> ProcesarAsync(
        EnvioJobForms envio, CancellationToken ct, string? rutaCvParaLimpiar = null)
    {
        try
        {
            var resultado = await recepcion.ProcesarAsync(envio, ct);

            // El envio ya estaba recibido (V27) pero igual se guardo un CV nuevo: no quedo
            // asociado a ninguna fila y es exactamente el huerfano que la invariante 9 prohibe.
            if (resultado.YaRecibido && rutaCvParaLimpiar is not null)
                await EliminarCvHuerfanoAsync(rutaCvParaLimpiar, ct);

            // YaRecibido le dice al Apps Script que su reintento llego a destino y que no hay nada
            // que volver a mandar (V27).
            return Ok(new { resultado.PostulanteId, resultado.PostulacionId, resultado.YaRecibido });
        }
        catch (InvalidOperationException ex)
        {
            // Vacante cerrada, consentimiento faltante o token desconocido. Son respuestas
            // esperables del negocio, no fallas del servidor: el formulario tiene que poder
            // mostrarle al postulante que paso. El DNI no va en el log (Regla 17): ex.Message no
            // lo repite, son mensajes fijos de ValidarEnvioAsync/RecepcionJobForms.
            if (rutaCvParaLimpiar is not null)
                await EliminarCvHuerfanoAsync(rutaCvParaLimpiar, ct);

            log.LogWarning(ex, "Envio de JobForms rechazado.");

            return UnprocessableEntity(new { motivo = ex.Message });
        }
        catch
        {
            // Cualquier otra falla tambien deja el CV huerfano si no se limpia aca: el catch de
            // InvalidOperationException no la cubre porque esto es lo inesperado, no un rechazo
            // del negocio. Se relanza igual: no es una respuesta que el controlador deba inventar.
            if (rutaCvParaLimpiar is not null)
                await EliminarCvHuerfanoAsync(rutaCvParaLimpiar, ct);

            throw;
        }
    }

    /// <summary>
    /// Mejor esfuerzo: si el borrado falla, se registra pero no tapa la respuesta original que ya
    /// se le debe al postulante. Un CV que no se pudo borrar queda huerfano en el recurso
    /// compartido (M8) y alguien tiene que enterarse por el log, no por un error distinto al que
    /// realmente paso.
    /// </summary>
    private async Task EliminarCvHuerfanoAsync(string rutaCv, CancellationToken ct)
    {
        try
        {
            await formularios.EliminarCvAsync(rutaCv, ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "No se pudo borrar el CV huerfano tras un envio de JobForms rechazado.");
        }
    }

    /// <summary>
    /// Delega la comparacion en <see cref="SecretoJobForms"/> —la misma que usa el limitador de
    /// velocidad para separar el cupo del script del de un desconocido (COR-15)— y solo agrega el
    /// log de configuracion faltante, que es lo unico que le concierne al controlador.
    /// </summary>
    private bool SecretoValido()
    {
        if (!_opciones.EstaConfigurado)
        {
            log.LogError("No hay secreto configurado para el webhook de JobForms. Se rechaza el envio.");
            return false;
        }

        return SecretoJobForms.EsValido(Request.Headers, _opciones);
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
