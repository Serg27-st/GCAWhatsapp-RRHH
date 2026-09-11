using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Worker;

/// <summary>
/// Deja la señal de vida del bucle sin que un fallo al escribirla tumbe el ciclo.
/// <para>
/// El latido es diagnóstico: si la base no responde, lo que importa es que el bucle siga
/// intentando su trabajo, no que muera por no poder anunciar que está vivo. La caída de la base
/// la reporta igual el health de la Api, que la comprueba por su cuenta.
/// </para>
/// </summary>
internal static class Latido
{
    public static async Task RegistrarAsync(
        IServiceScopeFactory ambitos,
        ILogger log,
        string servicio,
        TimeSpan tolerancia,
        string? detalle,
        CancellationToken ct)
    {
        try
        {
            using var ambito = ambitos.CreateScope();

            await ambito.ServiceProvider
                .GetRequiredService<ILatidoServicio>()
                .RegistrarAsync(servicio, tolerancia, detalle, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "No se pudo registrar el latido de {Servicio}.", servicio);
        }
    }
}
