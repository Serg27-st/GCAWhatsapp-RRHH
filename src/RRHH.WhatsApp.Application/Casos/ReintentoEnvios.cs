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
    ValidadorEnvio validador,
    DespachoEnvios despacho,
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

            // Revalida la Regla 15 justo antes de reenviar: la ventana pudo cerrarse entre el primer
            // intento y este, y un texto libre que era legal hace tres horas ahora no lo es.
            var motivoBloqueo = await validador.MotivoParaNoEnviarAsync(mensaje, ct);

            if (motivoBloqueo is not null)
            {
                await mensajes.MarcarEnvioFallidoAsync(
                    mensaje.MensajeId, motivoBloqueo, ClaseFallo.Permanente, null, ct);

                abandonados++;
                continue;
            }

            // Misma construccion que el despacho: asi tambien se reintentan botones y listas (T1.08).
            var resultado = await despacho.EnviarSegunTipoAsync(mensaje, ct);

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
    /// Retroceso exponencial: 1x, 2x, 4x. Si el proveedor esta caido, insistir cada pocos segundos
    /// no adelanta nada y suma trafico a un servicio que ya esta fallando.
    /// </summary>
    private static TimeSpan Retroceso(int baseSegundos, int intentosUsados) =>
        TimeSpan.FromSeconds(baseSegundos * Math.Pow(2, Math.Max(0, intentosUsados - 1)));

    private static int LeerEntero(IReadOnlyDictionary<string, string> config, string clave, int porDefecto) =>
        config.TryGetValue(clave, out var valor) && int.TryParse(valor, out var numero) ? numero : porDefecto;
}
