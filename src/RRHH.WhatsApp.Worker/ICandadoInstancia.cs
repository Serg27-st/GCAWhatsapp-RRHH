namespace RRHH.WhatsApp.Worker;

/// <summary>
/// El candado que garantiza una sola instancia activa del Worker (V24).
/// <para>
/// Es una interfaz local al Worker: existe para poder probar la guardia sin SQL Server, no es una
/// frontera entre modulos. La unica implementacion real es <see cref="CandadoSqlServer"/>.
/// </para>
/// </summary>
public interface ICandadoInstancia : IAsyncDisposable
{
    /// <summary>Intenta tomarlo sin esperar. False si lo tiene otra instancia.</summary>
    Task<bool> IntentarTomarAsync(CancellationToken ct);

    /// <summary>
    /// Si esta instancia lo sigue teniendo. Tambien es false cuando no se puede saber: ante la
    /// duda, no se procesa.
    /// </summary>
    Task<bool> SigueTomadoAsync(CancellationToken ct);

    /// <summary>Lo suelta de forma explicita y cierra la conexion.</summary>
    Task LiberarAsync();
}
