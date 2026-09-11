using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RRHH.WhatsApp.Application.Casos;

namespace RRHH.WhatsApp.Api.Controllers;

/// <summary>
/// Punto de entrada de los eventos de 360dialog. Es intencionalmente delgado: valida, normaliza,
/// encola y responde. Toda la logica de negocio ocurre despues, al consumir la outbox.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("webhook/whatsapp")]
public sealed class WebhookWhatsAppController(
    RecepcionWebhook recepcion,
    ILogger<WebhookWhatsAppController> log) : ControllerBase
{
    /// <summary>
    /// Verificacion del endpoint. Meta y algunos BSP hacen un GET con un desafio antes de empezar
    /// a entregar eventos; se devuelve el mismo valor recibido.
    /// </summary>
    [HttpGet]
    public IActionResult Verificar(
        [FromQuery(Name = "hub.mode")] string? modo,
        [FromQuery(Name = "hub.verify_token")] string? token,
        [FromQuery(Name = "hub.challenge")] string? desafio,
        [FromServices] IConfiguration configuracion)
    {
        // Sirve para los dos proveedores: Meta Cloud API directo y 360dialog. Se toma el que este
        // configurado, para no obligar a repetir el mismo valor en dos lugares.
        var esperado = configuracion["MetaCloud:TokenVerificacion"];

        if (string.IsNullOrWhiteSpace(esperado))
            esperado = configuracion["Dialog360:TokenVerificacion"];

        if (string.IsNullOrWhiteSpace(esperado) || token != esperado)
        {
            log.LogWarning("Intento de verificacion del webhook con token invalido.");
            // 403 explicito y no Forbid(): sin un esquema de autenticacion registrado, Forbid()
            // lanza y Meta veria un 500 donde espera un rechazo limpio.
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        return modo == "subscribe" ? Content(desafio ?? string.Empty) : BadRequest();
    }

    [HttpPost]
    public async Task<IActionResult> Recibir(CancellationToken ct)
    {
        // El cuerpo tiene que leerse crudo: la firma se calcula sobre los bytes exactos que
        // llegaron. Deserializar y volver a serializar la invalida.
        string cuerpo;
        using (var lector = new StreamReader(Request.Body, leaveOpen: false))
            cuerpo = await lector.ReadToEndAsync(ct);

        var cabeceras = Request.Headers
            .ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        var resultado = await recepcion.ProcesarAsync(cuerpo, cabeceras, ct);

        if (!resultado.FirmaValida)
            return Unauthorized();

        // Siempre 200 cuando la firma es valida, incluso si no habia nada que hacer. Un codigo
        // distinto hace que 360dialog reintente la entrega de algo que ya quedo resuelto.
        return Ok(new
        {
            recibidos = resultado.MensajesNuevos,
            duplicados = resultado.MensajesDuplicados,
            acuses = resultado.EstadosActualizados
        });
    }
}
