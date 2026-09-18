using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Application.Reglas;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Casos;

/// <summary>
/// Lo que la bandeja necesita saber tras un intento de respuesta. <see cref="RequierePlantilla"/>
/// distingue el rechazo que se resuelve eligiendo una plantilla del que no tiene salida.
/// </summary>
public sealed record ResultadoRespuestaAnalista(
    bool Enviado, string? Motivo, bool RequierePlantilla, long? MensajeId);

/// <summary>
/// El analista responde por la bandeja. Es el unico camino por el que sale un mensaje escrito por
/// una persona, y por eso es donde la Regla 15 tiene efecto real: sin opt-in no sale nada, y fuera
/// de la ventana de 24h solo puede salir una plantilla aprobada por Meta.
/// <para>
/// Corre sincronico y no por la outbox a proposito (V14): el analista tiene que enterarse en el acto
/// de que su mensaje no salio, no descubrirlo despues en un log.
/// </para>
/// <para>
/// Registra la fila antes de llamar al proveedor, con la clave que genera la bandeja al redactar
/// (V29). Un doble clic, o la bandeja reintentando porque perdio la respuesta, devuelve el mensaje
/// que ya existe en vez de mandar otro. La fila nace Enviando: el despachador del Worker solo toma
/// EnCola, asi que no puede mandarla por su cuenta mientras esto la envia.
/// </para>
/// </summary>
public sealed class EnvioAnalista(
    IConversacionService conversaciones,
    IMensajeService mensajes,
    IPlantillaService plantillas,
    IEventoSistemaService eventos,
    IAuditoriaService auditoria,
    IFabricaContextoRegla fabrica,
    IMotorReglas motor,
    DespachoEnvios despacho,
    TimeProvider reloj,
    ILogger<EnvioAnalista> log)
{
    /// <param name="claveIdempotencia">
    /// La genera la bandeja por mensaje redactado. <see cref="Guid.Empty"/> o nula significa que el
    /// cliente no la manda: cada llamada es un envio distinto, como antes de la cola.
    /// </param>
    public async Task<ResultadoRespuestaAnalista> ResponderAsync(
        int conversacionId,
        int analistaId,
        string? texto,
        string? clavePlantilla,
        IReadOnlyList<string>? parametros,
        Guid? claveIdempotencia = null,
        CancellationToken ct = default)
    {
        var clave = claveIdempotencia is { } g && g != Guid.Empty
            ? $"ana:{g:N}"
            : $"ana:{Guid.NewGuid():N}";

        // Ya se proceso este mismo mensaje: se devuelve lo que paso, sin volver a evaluar ni enviar.
        if (await mensajes.ObtenerPorClaveIdempotenciaAsync(clave, ct) is { } previo)
            return ResultadoDe(previo);

        var conversacion = await conversaciones.ObtenerPorIdAsync(conversacionId, ct)
            ?? throw new InvalidOperationException($"No existe la conversacion {conversacionId}.");

        // FUN-01 (P3): responder sin tomar dejaria un hilo contestado que sigue figurando sin dueño
        // en la bandeja general, y con el bot todavia a cargo del menu (V30).
        if (conversacion.Estado is EstadoConversacion.PendienteClasificar or EstadoConversacion.EnMenuBot)
        {
            return new(false, MotivosBandeja.TomarPrimero, RequierePlantilla: false, null);
        }

        var correlationId = Guid.NewGuid();
        var contexto = await fabrica.ParaEnvioSalienteAsync(conversacionId, correlationId, clave, ct);

        var acciones = await motor.ProcesarAsync(contexto, ct);

        // Sin opt-in registrado no hay envio posible, ni con plantilla. Es el caso que Meta
        // penaliza como mensaje no solicitado y el que causo el bloqueo original.
        if (acciones.OfType<BloquearEnvio>().FirstOrDefault() is { } bloqueo)
        {
            log.LogWarning("Respuesta bloqueada en la conversacion {ConversacionId}: {Motivo}",
                conversacionId, bloqueo.Motivo);

            return new(false, bloqueo.Motivo, RequierePlantilla: false, null);
        }

        // Los eventos que produjo alguna regla se publican igual, como en cualquier evaluacion.
        foreach (var aviso in acciones.OfType<PublicarEvento>())
            await eventos.PublicarAsync(aviso.Tipo, aviso.Payload, correlationId, ct);

        var ventanaCerrada = acciones.OfType<RequierePlantilla>().Any();

        if (ventanaCerrada && string.IsNullOrWhiteSpace(clavePlantilla))
        {
            // Un intento de texto libre fuera de la ventana es justo lo que conviene poder revisar
            // despues, con quien lo intento. Antes era un evento de la outbox que nadie consumia (V32).
            await auditoria.RegistrarAsync(
                nameof(Conversacion), conversacionId.ToString(), analistaId, "EnvioRequierePlantilla",
                "Respuesta en texto libre rechazada: la ventana de 24h estaba cerrada (Regla 15).", ct);

            return new(false,
                "Pasaron mas de 24 horas desde el ultimo mensaje del postulante: solo se puede " +
                "responder con una plantilla aprobada por Meta.",
                RequierePlantilla: true, null);
        }

        var saliente = string.IsNullOrWhiteSpace(clavePlantilla)
            // FUN-09 (A3, R11): con procesos vivos en dos cuentas, el texto dice de cual habla. La
            // plantilla no lo lleva: su contenido esta aprobado por Meta y no se puede tocar.
            ? ArmarTexto(PrefijoMultiCuenta.Aplicar(
                texto ?? string.Empty, contexto.PostulacionesDelPostulante, conversacion.CuentaContextoId))
            : await ArmarPlantillaAsync(clavePlantilla, parametros ?? [], ct);

        if (saliente.Rechazo is { } rechazo)
            return rechazo;

        var mensaje = await mensajes.EncolarSalienteAsync(
            conversacion.ConversacionId, saliente.Saliente!, clave, analistaId, correlationId,
            reservadoParaEnvio: true, ct);

        // Otra peticion con la misma clave gano la carrera entre la consulta de arriba y el guardado.
        if (mensaje is null)
        {
            return await mensajes.ObtenerPorClaveIdempotenciaAsync(clave, ct) is { } ganador
                ? ResultadoDe(ganador)
                : new(false, "No se pudo registrar el mensaje.", RequierePlantilla: false, null);
        }

        var resultado = await despacho.EnviarSegunTipoAsync(mensaje, ct);

        if (!resultado.Exito)
        {
            log.LogError("Fallo la respuesta del analista {AnalistaId} en la conversacion {ConversacionId}: {Error}",
                analistaId, conversacion.ConversacionId, resultado.Error);

            // El analista ve el motivo en la respuesta, pero el hilo tambien tiene que mostrarlo.
            // No se agenda reintento automatico: el analista esta mirando la pantalla y decide si
            // reescribe o vuelve a intentar, que es mejor que un reenvio a sus espaldas.
            await mensajes.MarcarEnvioFallidoAsync(mensaje.MensajeId, resultado.Error, resultado.Clase, null, ct);

            return new(false, resultado.Error, RequierePlantilla: false, mensaje.MensajeId);
        }

        await mensajes.MarcarEnvioLogradoAsync(mensaje.MensajeId, resultado.ProviderMessageId, ct);

        // Detiene el reloj de la Regla 2. Sin esta marca el Worker escalaria una conversacion que
        // el analista acaba de atender.
        await conversaciones.RegistrarRespuestaAnalistaAsync(
            conversacion.ConversacionId, reloj.GetUtcNow().UtcDateTime, ct);

        return new(true, null, RequierePlantilla: false, mensaje.MensajeId);
    }

    private static (SalienteEncolado? Saliente, ResultadoRespuestaAnalista? Rechazo) ArmarTexto(string? texto) =>
        string.IsNullOrWhiteSpace(texto)
            ? (null, new(false, "El mensaje esta vacio.", RequierePlantilla: false, null))
            : (new SalienteEncolado(TipoSaliente.Texto, texto), null);

    private async Task<(SalienteEncolado? Saliente, ResultadoRespuestaAnalista? Rechazo)> ArmarPlantillaAsync(
        string clave, IReadOnlyList<string> parametros, CancellationToken ct)
    {
        // Nula si no existe o si sigue inactiva por falta de aprobacion en Meta. El analista tiene
        // que saber cual de las dos cosas pasa, porque ninguna la resuelve reintentando.
        var plantilla = await plantillas.ObtenerPlantillaParaEventoAsync(clave, ct);

        if (plantilla is null)
        {
            return (null, new(false,
                $"La plantilla '{clave}' no existe en el catalogo o todavia no fue aprobada por Meta.",
                RequierePlantilla: false, null));
        }

        if (plantilla.CantidadParametros != parametros.Count)
        {
            return (null, new(false,
                $"La plantilla '{clave}' espera {plantilla.CantidadParametros} parametro(s) y se enviaron {parametros.Count}.",
                RequierePlantilla: false, null));
        }

        return (new SalienteEncolado(
            TipoSaliente.Plantilla, plantilla.TextoAprobado, plantilla.PlantillaId, parametros), null);
    }

    /// <summary>Lo que ya paso con un mensaje de la misma clave, en los terminos que entiende la bandeja.</summary>
    private static ResultadoRespuestaAnalista ResultadoDe(Mensaje mensaje) => mensaje.EstadoEntrega switch
    {
        EstadoEntrega.Fallido => new(false, mensaje.ErrorProveedor, RequierePlantilla: false, mensaje.MensajeId),

        // Otra peticion con la misma clave lo esta enviando ahora: no se manda de nuevo.
        EstadoEntrega.Enviando or EstadoEntrega.EnCola =>
            new(false, "Este mensaje ya se esta enviando.", RequierePlantilla: false, mensaje.MensajeId),

        _ => new(true, null, RequierePlantilla: false, mensaje.MensajeId)
    };
}
