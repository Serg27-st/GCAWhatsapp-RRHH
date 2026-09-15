using System.Text.Json;
using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Application.Casos;

public sealed record ResumenReintentos(int Intentados, int Logrados, int Reprogramados, int Abandonados)
{
    public static readonly ResumenReintentos Vacio = new(0, 0, 0, 0);

    public override string ToString() =>
        $"{Intentados} intento(s): {Logrados} logrado(s), {Reprogramados} reprogramado(s), {Abandonados} abandonado(s).";
}

/// <summary>
/// Reintenta los salientes que el proveedor rechazo sin llegar a procesarlos.
/// <para>
/// El reintento es por mensaje y no por evento de la outbox a proposito. Reprocesar el evento
/// volveria a correr todas las reglas —reasignar, reauditar, reenviar lo que si habia salido—, que
/// es exactamente el camino a duplicar mensajes. Aca se reintenta una sola cosa: el envio que
/// falto.
/// </para>
/// <para>
/// Solo entra <see cref="ClaseFallo.Transitorio"/>. Lo permanente no cambia reintentando, y lo
/// ambiguo pudo haber salido: la Cloud API no admite clave de idempotencia, asi que reintentarlo
/// arriesga entregar el mismo mensaje dos veces.
/// </para>
/// </summary>
public sealed class ReintentoEnvios(
    IMensajeService mensajes,
    IConversacionService conversaciones,
    IPlantillaService plantillas,
    IWhatsAppProvider proveedor,
    IConfiguracionReglasService configuracion,
    TimeProvider reloj,
    ILogger<ReintentoEnvios> log)
{
    public async Task<ResumenReintentos> ProcesarAsync(int maximo, CancellationToken ct = default)
    {
        // ARQ-01: el retroceso exponencial (1m, 2m, 4m) se agenda contra este instante; con el
        // reloj inyectable el reintento se puede probar sin esperar minutos reales.
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var pendientes = await mensajes.ListarPendientesDeReintentoAsync(maximo, ahora, ct);

        if (pendientes.Count == 0)
            return ResumenReintentos.Vacio;

        var config = await configuracion.ObtenerTodasAsync(ct);
        var maximoIntentos = LeerEntero(config, ClavesConfiguracion.EnvioReintentosMaximos, 4);
        var baseSegundos = LeerEntero(config, ClavesConfiguracion.EnvioReintentoBaseSegundos, 60);

        var logrados = 0;
        var reprogramados = 0;
        var abandonados = 0;

        foreach (var mensaje in pendientes)
        {
            ct.ThrowIfCancellationRequested();

            var motivoBloqueo = await MotivoParaNoReintentarAsync(mensaje, ct);

            if (motivoBloqueo is not null)
            {
                await mensajes.MarcarEnvioFallidoAsync(
                    mensaje.MensajeId, motivoBloqueo, ClaseFallo.Permanente, null, ct);

                abandonados++;
                continue;
            }

            var resultado = await ReenviarAsync(mensaje, ct);

            if (resultado.Exito)
            {
                await mensajes.MarcarEnvioLogradoAsync(mensaje.MensajeId, resultado.ProviderMessageId, ct);

                log.LogInformation("El mensaje {MensajeId} salio en el reintento {Intento}.",
                    mensaje.MensajeId, mensaje.IntentosEnvio + 1);

                logrados++;
                continue;
            }

            var intentosUsados = mensaje.IntentosEnvio + 1;

            // Se reprograma solo si sigue siendo transitorio y quedan intentos. Si el fallo cambio
            // de clase —por ejemplo el token vencio entre un intento y otro— se corta aca en vez de
            // seguir golpeando a Meta con algo que ya no va a funcionar.
            var puedeSeguir = resultado.Clase == ClaseFallo.Transitorio && intentosUsados < maximoIntentos;

            await mensajes.MarcarEnvioFallidoAsync(
                mensaje.MensajeId,
                resultado.Error,
                resultado.Clase,
                puedeSeguir ? ahora.Add(Retroceso(baseSegundos, intentosUsados)) : null,
                ct);

            if (puedeSeguir)
            {
                reprogramados++;
            }
            else
            {
                abandonados++;

                log.LogError(
                    "El mensaje {MensajeId} no se reintenta mas ({Clase}, {Intentos} intento(s)): {Error}",
                    mensaje.MensajeId, resultado.Clase, intentosUsados, resultado.Error);
            }
        }

        return new ResumenReintentos(pendientes.Count, logrados, reprogramados, abandonados);
    }

    /// <summary>
    /// Revalida la Regla 15 justo antes de reenviar. Es el punto que hace seguro el reintento: la
    /// ventana de 24h pudo haberse cerrado entre el primer intento y este, y un texto libre que
    /// era legal hace tres horas ahora no lo es. Devuelve el motivo por el que hay que abandonar,
    /// o nulo si se puede seguir.
    /// </summary>
    private async Task<string?> MotivoParaNoReintentarAsync(Mensaje mensaje, CancellationToken ct)
    {
        var conversacion = mensaje.Conversacion
            ?? await conversaciones.ObtenerPorIdAsync(mensaje.ConversacionId, ct);

        if (conversacion is null)
            return "La conversacion ya no existe.";

        if (conversacion.FechaOptIn is null)
            return "Ya no hay opt-in registrado para este numero (Regla 15).";

        // Con plantilla el envio es valido con la ventana cerrada; en texto libre, no.
        if (mensaje.PlantillaId is null
            && !await plantillas.ValidarVentana24hAsync(conversacion.ConversacionId, ct))
        {
            return "La ventana de 24h se cerro mientras el mensaje esperaba: "
                 + "reenviarlo como texto libre violaria la Regla 15.";
        }

        return null;
    }

    private async Task<ResultadoEnvio> ReenviarAsync(Mensaje mensaje, CancellationToken ct)
    {
        var telefono = mensaje.Conversacion?.TelefonoE164
            ?? (await conversaciones.ObtenerPorIdAsync(mensaje.ConversacionId, ct))?.TelefonoE164;

        if (telefono is null)
            return ResultadoEnvio.Permanente("La conversacion ya no existe.");

        if (mensaje.PlantillaId is null)
            return await proveedor.EnviarTextoAsync(telefono, mensaje.Contenido, ct);

        var plantilla = mensaje.Plantilla
            ?? await plantillas.ObtenerPlantillaParaEventoAsync(mensaje.Plantilla?.Clave ?? string.Empty, ct);

        // Nula significa que la plantilla se desactivo o dejo de estar aprobada desde el primer
        // intento. Reintentarla es justamente lo que provoca sanciones sobre la linea.
        if (plantilla is null || !plantilla.Activa)
            return ResultadoEnvio.Permanente("La plantilla ya no esta activa o aprobada en Meta.");

        return await proveedor.EnviarPlantillaAsync(telefono, plantilla, LeerParametros(mensaje), ct);
    }

    /// <summary>
    /// Los parametros se guardan al enviar porque el contenido almacenado conserva los {{n}} sin
    /// reemplazar: sin ellos no se podria rearmar la plantilla.
    /// </summary>
    private IReadOnlyList<string> LeerParametros(Mensaje mensaje)
    {
        if (string.IsNullOrWhiteSpace(mensaje.ParametrosPlantillaJson))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<string>>(mensaje.ParametrosPlantillaJson) ?? [];
        }
        catch (JsonException ex)
        {
            log.LogError(ex, "No se pudieron leer los parametros del mensaje {MensajeId}.", mensaje.MensajeId);
            return [];
        }
    }

    /// <summary>
    /// Retroceso exponencial: 1x, 2x, 4x. Si el proveedor esta caido, insistir cada pocos segundos
    /// no adelanta nada y suma trafico a un servicio que ya esta fallando.
    /// </summary>
    private static TimeSpan Retroceso(int baseSegundos, int intentosUsados) =>
        TimeSpan.FromSeconds(baseSegundos * Math.Pow(2, Math.Max(0, intentosUsados - 1)));

    private static int LeerEntero(IReadOnlyDictionary<string, string> config, string clave, int porDefecto) =>
        config.TryGetValue(clave, out var valor) && int.TryParse(valor, out var numero) ? numero : porDefecto;
}
