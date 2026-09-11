namespace RRHH.WhatsApp.Worker;

internal static class EsperaSegura
{
    /// <summary>
    /// Espera absorbiendo la cancelacion. Al detener el servicio, el <c>Task.Delay</c> en curso
    /// lanza; que eso suba como excepcion convierte un apagado normal en un error en el log.
    /// </summary>
    public static async Task DormirAsync(TimeSpan espera, CancellationToken ct)
    {
        try
        {
            await Task.Delay(espera, ct);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
