using RRHH.WhatsApp.Api.Seguridad;
using Microsoft.AspNetCore.Mvc;
using RRHH.WhatsApp.Api.Mapeo;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Contracts.Bandeja;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Excepciones;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Api.Controllers;

/// <summary>
/// La bandeja del analista (Seccion 7). El analista sale del token, y toda accion con <c>{id}</c>
/// pasa antes por <see cref="FiltroAccesoConversacion"/>: tener sesion no alcanza para leer o
/// responder una conversacion ajena (Regla 4, V22 de docs/decisiones.md).
/// </summary>
[ApiController]
[Route("conversaciones")]
[TypeFilter<FiltroAccesoConversacion>]
public sealed class ConversacionesController(
    IConversacionService conversaciones,
    IMensajeService mensajes,
    IPostulacionService postulaciones,
    EnvioAnalista envio,
    AccionesBandeja acciones,
    IAlmacenamientoAdjuntos adjuntos,
    TimeProvider reloj,
    ILogger<ConversacionesController> log) : ControllerBase
{
    /// <summary>
    /// ARQ-01: el mapeo recibe el instante en vez de leer el reloj del sistema. Se lee una vez por
    /// peticion, asi todos los hilos de una lista se comparan contra el mismo momento.
    /// </summary>
    private DateTime Ahora() => reloj.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Regla 4: el analista ve solo lo suyo; el rol Sistemas ve todo, para soporte y auditoria.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct)
    {
        try
        {
            var hilos = await conversaciones.ListarParaAnalistaAsync(User.AnalistaId(), ct);
            var ahora = Ahora();

            return Ok(hilos.Select(c => c.AResumen(ahora)));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { motivo = ex.Message });
        }
    }

    /// <summary>Regla 19: bandeja general de pendientes por clasificar, visible para todos.</summary>
    [HttpGet("pendientes")]
    public async Task<IActionResult> Pendientes(CancellationToken ct)
    {
        var hilos = await conversaciones.ListarPendientesClasificarAsync(ct);
        var ahora = Ahora();

        return Ok(hilos.Select(c => c.AResumen(ahora)));
    }

    /// <summary>
    /// Buscador por DNI de la Seccion 7: trae directamente el chat del postulante, si es de quien
    /// busca (Reglas 4 y 6). Sistemas los encuentra todos.
    /// </summary>
    [HttpGet("buscar")]
    public async Task<IActionResult> Buscar([FromQuery] string dni, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dni))
            return BadRequest(new { motivo = "Falta el DNI." });

        // T0.07a: el JobForms guarda el DNI normalizado. Buscar con lo que escribio el analista
        // —con puntos, guiones o en minusculas— no encontraria a la persona.
        if (!DocumentoIdentidad.TryNormalizar(dni, out var normalizado))
            return BadRequest(new { motivo = "El DNI no tiene un formato valido." });

        var hilos = await conversaciones.BuscarPorDniAsync(normalizado, User.AnalistaId(), ct);
        var ahora = Ahora();

        return Ok(hilos.Select(c => c.AResumen(ahora)));
    }

    /// <summary>El chat completo, con el aviso multi-cuenta de la Regla 6 en la cabecera.</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detalle(int id, CancellationToken ct)
    {
        var conversacion = await conversaciones.ObtenerPorIdAsync(id, ct);

        if (conversacion is null)
            return NotFound();

        var hilo = await mensajes.ListarPorConversacionAsync(id, ct: ct);

        // Regla 6: aviso generico de que el mismo DNI esta en proceso con otras cuentas, sin
        // dejar ver el detalle de esas conversaciones.
        var otrasCuentas = conversacion.PostulanteId is { } postulanteId && conversacion.CuentaContextoId is { } cuentaId
            ? (await postulaciones.ObtenerOtrasCuentasEnProcesoAsync(postulanteId, cuentaId, ct))
                .Select(c => c.Nombre).ToList()
            : [];

        // Regla 6 otra vez: las postulaciones de la persona en otras cuentas son de otros
        // analistas. De esas ya esta el aviso generico de arriba; el detalle —vacante, etapa— se
        // limita a la cuenta sobre la que se conversa. Sistemas ve todas (Regla 4).
        var visibles = conversacion.PostulanteId is { } persona
            ? (await postulaciones.ObtenerTableroPorPostulanteAsync(persona, ct))
                .Where(p => User.EsSistemas() || p.CuentaId == conversacion.CuentaContextoId)
                .Select(p => p.AResumen())
                .ToList()
            : [];

        // El filtro ya dejo el nivel. Lectura es Sistemas mirando algo que atiende otro.
        var soloLectura = HttpContext.Items[FiltroAccesoConversacion.ClaveNivel] is not NivelAcceso.Total;

        return Ok(new ConversacionDetalle(
            conversacion.AResumen(Ahora(), otrasCuentas),
            [.. hilo.Select(m => m.AResumen())],
            visibles,
            soloLectura));
    }

    /// <summary>
    /// FUN-14 (V33): el archivo que mando el postulante. Pasa por el mismo filtro que el chat (Regla 4)
    /// y solo se entrega si ya paso el antivirus y es de esta conversacion: el id del adjunto solo no
    /// alcanza para pedir el de otra.
    /// </summary>
    [HttpGet("{id:int}/adjuntos/{adjuntoId:long}")]
    public async Task<IActionResult> Adjunto(int id, long adjuntoId, CancellationToken ct)
    {
        var adjunto = await mensajes.ObtenerAdjuntoAsync(adjuntoId, id, ct);

        if (adjunto is not { Estado: EstadoAdjunto.Descargado, Ruta: { } ruta })
            return NotFound(new { motivo = "El archivo no esta disponible." });

        var contenido = await adjuntos.ObtenerAsync(ruta, ct);

        if (contenido is null)
        {
            log.LogWarning("El adjunto {AdjuntoId} figura descargado pero su archivo no esta.", adjuntoId);
            return NotFound(new { motivo = "El archivo no esta disponible." });
        }

        // El nombre lo puso el postulante: sirve para mostrar, pero la extension es la del archivo
        // guardado, que es la que paso los controles (V33).
        var nombre = Path.GetFileNameWithoutExtension(adjunto.NombreArchivo ?? $"adjunto-{adjuntoId}")
            + Path.GetExtension(ruta);

        return File(contenido, adjunto.MimeType, nombre);
    }

    /// <summary>Regla 15: es aca donde se aplica el limite de opt-in y la ventana de 24h.</summary>
    [HttpPost("{id:int}/responder")]
    public async Task<IActionResult> Responder(
        int id, [FromBody] PeticionResponder peticion, CancellationToken ct)
    {
        try
        {
            var resultado = await envio.ResponderAsync(
                id, User.AnalistaId(), peticion.Texto, peticion.ClavePlantilla, peticion.Parametros,
                peticion.ClaveIdempotencia, ct);

            var respuesta = new ResultadoResponder(
                resultado.Enviado, resultado.Motivo, resultado.RequierePlantilla, resultado.MensajeId);

            // 422 y no 400: la peticion es valida, lo que no corresponde es el envio.
            return resultado.Enviado ? Ok(respuesta) : UnprocessableEntity(respuesta);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { motivo = ex.Message });
        }
    }


    /// <summary>
    /// FUN-01 (AL1, P3): el analista se adjudica un hilo de «Sin clasificar» para una de sus cuentas.
    /// Es la unica salida de esa bandeja, y por eso corre con nivel Lectura (<see cref="PermiteTomarAttribute"/>).
    /// </summary>
    [HttpPost("{id:int}/tomar")]
    [PermiteTomar]
    public async Task<IActionResult> Tomar(int id, [FromBody] PeticionTomar peticion, CancellationToken ct)
    {
        try
        {
            var conversacion = await conversaciones.TomarAsync(id, User.AnalistaId(), peticion.CuentaId, ct);

            return Ok(conversacion.AResumen(reloj.GetUtcNow().UtcDateTime));
        }
        catch (ConflictoConcurrenciaException ex)
        {
            // Otro llego primero: la pantalla tiene que refrescarse y mostrar que ya no esta libre.
            return Conflict(new { motivo = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { motivo = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { motivo = ex.Message });
        }
    }
    /// <summary>Regla 8: transferencia manual a otro analista, de uno en uno.</summary>
    [HttpPost("{id:int}/transferir")]
    public async Task<IActionResult> Transferir(
        int id, [FromBody] PeticionTransferir peticion, CancellationToken ct)
    {
        try
        {
            var transferencia = await conversaciones.TransferirAsync(
                id, User.AnalistaId(), peticion.AnalistaDestinoId,
                peticion.Urgente, peticion.Comentario, ct);

            return Ok(new
            {
                transferencia.TransferenciaId,
                estado = transferencia.Estado.ToString(),
                transferencia.Urgente
            });
        }
        catch (InvalidOperationException ex)
        {
            log.LogWarning(ex, "Transferencia rechazada en la conversacion {ConversacionId}.", id);
            return UnprocessableEntity(new { motivo = ex.Message });
        }
    }

    /// <summary>Regla 7: whitelist (motivo opcional) o blacklist (motivo obligatorio).</summary>
    [HttpPost("{id:int}/marcar")]
    public async Task<IActionResult> Marcar(
        int id, [FromBody] PeticionMarcar peticion, CancellationToken ct)
    {
        if (!Enum.TryParse<TipoEstadoPostulante>(peticion.Tipo, ignoreCase: true, out var tipo))
            return BadRequest(new { motivo = $"Tipo '{peticion.Tipo}' desconocido. Use Whitelist o Blacklist." });

        // El filtro garantiza que la conversacion es de quien marca, pero el postulante y la cuenta
        // llegan en el cuerpo. Sin contrastarlos, bastaria abrir un chat propio para descartar a
        // alguien en la cuenta de otro analista (Reglas 4 y 6).
        var conversacion = await conversaciones.ObtenerPorIdAsync(id, ct);

        if (conversacion is null
            || conversacion.PostulanteId != peticion.PostulanteId
            || conversacion.CuentaContextoId != peticion.CuentaId)
        {
            return UnprocessableEntity(new { motivo = "El postulante o la cuenta no corresponden a esta conversación." });
        }

        // FUN-01 (P3): marcar es actuar sobre el caso; primero hay que tomarlo de la bandeja general.
        if (conversacion.Estado is EstadoConversacion.PendienteClasificar or EstadoConversacion.EnMenuBot)
            return UnprocessableEntity(new { motivo = MotivosBandeja.TomarPrimero });

        try
        {
            await acciones.MarcarAsync(
                peticion.PostulanteId, peticion.CuentaId, tipo, peticion.Motivo, User.AnalistaId(),
                peticion.EnviarCierre, ct);

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { motivo = ex.Message });
        }
    }
}
