using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Application.Reglas;
using RRHH.WhatsApp.Domain.Entidades;
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
/// Corre sincronico y no por la outbox a proposito: el analista tiene que enterarse en el acto de
/// que su mensaje no salio, no descubrirlo despues en un log.
/// </para>
/// </summary>
public sealed class EnvioAnalista(
    IWhatsAppProvider proveedor,
    IConversacionService conversaciones,
    IMensajeService mensajes,
    IPlantillaService plantillas,
    IEventoSistemaService eventos,
    IFabricaContextoRegla fabrica,
    IMotorReglas motor,
    ILogger<EnvioAnalista> log)
{
    public async Task<ResultadoRespuestaAnalista> ResponderAsync(
        int conversacionId,
        int analistaId,
        string? texto,
        string? clavePlantilla,
        IReadOnlyList<string>? parametros,
        CancellationToken ct = default)
    {
        var conversacion = await conversaciones.ObtenerPorIdAsync(conversacionId, ct)
            ?? throw new InvalidOperationException($"No existe la conversacion {conversacionId}.");

        var correlationId = Guid.NewGuid();
        var contexto = await fabrica.ParaEnvioSalienteAsync(conversacionId, correlationId, ct);

        var acciones = await motor.ProcesarAsync(contexto, ct);

        // Sin opt-in registrado no hay envio posible, ni con plantilla. Es el caso que Meta
        // penaliza como mensaje no solicitado y el que causo el bloqueo original.
        if (acciones.OfType<BloquearEnvio>().FirstOrDefault() is { } bloqueo)
        {
            log.LogWarning("Respuesta bloqueada en la conversacion {ConversacionId}: {Motivo}",
                conversacionId, bloqueo.Motivo);

            return new(false, bloqueo.Motivo, RequierePlantilla: false, null);
        }

        // Los avisos que produjo la regla se publican igual: un intento fuera de ventana es
        // justamente lo que conviene poder auditar despues.
        foreach (var aviso in acciones.OfType<PublicarEvento>())
            await eventos.PublicarAsync(aviso.Tipo, aviso.Payload, correlationId, ct);

        var ventanaCerrada = acciones.Any(
            a => a is PublicarEvento { Tipo: TiposEvento.EnvioRequierePlantilla });

        if (ventanaCerrada && string.IsNullOrWhiteSpace(clavePlantilla))
        {
            return new(false,
                "Pasaron mas de 24 horas desde el ultimo mensaje del postulante: solo se puede " +
                "responder con una plantilla aprobada por Meta.",
                RequierePlantilla: true, null);
        }

        return string.IsNullOrWhiteSpace(clavePlantilla)
            ? await EnviarTextoAsync(conversacion, analistaId, texto, correlationId, ct)
            : await EnviarPlantillaAsync(conversacion, analistaId, clavePlantilla, parametros ?? [], correlationId, ct);
    }

    private async Task<ResultadoRespuestaAnalista> EnviarTextoAsync(
        Conversacion conversacion, int analistaId, string? texto, Guid correlationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return new(false, "El mensaje esta vacio.", RequierePlantilla: false, null);

        var resultado = await proveedor.EnviarTextoAsync(conversacion.TelefonoE164, texto, ct);

        return await RegistrarAsync(conversacion, analistaId, texto, null, resultado, correlationId, ct);
    }

    private async Task<ResultadoRespuestaAnalista> EnviarPlantillaAsync(
        Conversacion conversacion, int analistaId, string clave,
        IReadOnlyList<string> parametros, Guid correlationId, CancellationToken ct)
    {
        // Nula si no existe o si sigue inactiva por falta de aprobacion en Meta. El analista tiene
        // que saber cual de las dos cosas pasa, porque ninguna la resuelve reintentando.
        var plantilla = await plantillas.ObtenerPlantillaParaEventoAsync(clave, ct);

        if (plantilla is null)
        {
            return new(false,
                $"La plantilla '{clave}' no existe en el catalogo o todavia no fue aprobada por Meta.",
                RequierePlantilla: false, null);
        }

        if (plantilla.CantidadParametros != parametros.Count)
        {
            return new(false,
                $"La plantilla '{clave}' espera {plantilla.CantidadParametros} parametro(s) y se enviaron {parametros.Count}.",
                RequierePlantilla: false, null);
        }

        var resultado = await proveedor.EnviarPlantillaAsync(
            conversacion.TelefonoE164, plantilla, parametros, ct);

        return await RegistrarAsync(
            conversacion, analistaId, plantilla.TextoAprobado, plantilla.PlantillaId,
            resultado, correlationId, ct, parametros);
    }

    private async Task<ResultadoRespuestaAnalista> RegistrarAsync(
        Conversacion conversacion, int analistaId, string contenido, int? plantillaId,
        ResultadoEnvio resultado, Guid correlationId, CancellationToken ct,
        IReadOnlyList<string>? parametrosPlantilla = null)
    {
        var mensaje = await mensajes.RegistrarSalienteAsync(
            conversacion.ConversacionId, contenido, plantillaId, analistaId,
            resultado.ProviderMessageId, correlationId, parametrosPlantilla, ct);

        if (!resultado.Exito)
        {
            log.LogError("Fallo la respuesta del analista {AnalistaId} en la conversacion {ConversacionId}: {Error}",
                analistaId, conversacion.ConversacionId, resultado.Error);

            // El analista ve el motivo en la respuesta, pero el hilo tambien tiene que mostrarlo:
            // sin esto el mensaje queda en Pendiente para siempre y nadie sabe por que no salio.
            // No se agenda reintento automatico: el analista esta mirando la pantalla y decide si
            // reescribe o vuelve a intentar, que es mejor que un reenvio a sus espaldas.
            await mensajes.MarcarEnvioFallidoAsync(
                mensaje.MensajeId, resultado.Error, resultado.Clase, null, ct);

            return new(false, resultado.Error, RequierePlantilla: false, mensaje.MensajeId);
        }

        // Detiene el reloj de la Regla 2. Sin esta marca el Worker escalaria una conversacion que
        // el analista acaba de atender.
        await conversaciones.RegistrarRespuestaAnalistaAsync(
            conversacion.ConversacionId, DateTime.UtcNow, ct);

        return new(true, null, RequierePlantilla: false, mensaje.MensajeId);
    }
}
