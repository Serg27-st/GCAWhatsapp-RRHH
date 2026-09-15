namespace RRHH.WhatsApp.Tests.Proveedores;

/// <summary>
/// Handler HTTP de prueba para T0.03 (ARQ-04/C4): reemplaza al handler real para que las pruebas
/// de <see cref="MetaCloudProviderTests"/>, <see cref="Dialog360ProviderTests"/> y de composición
/// controlen exactamente qué responde o lanza la red, y puedan contar cuántas peticiones salieron.
/// <para>
/// Compartido entre proveedores porque ambos usan el mismo <c>HttpClient</c> por debajo (360dialog
/// es un paso a través de la Cloud API de Meta) y las pruebas de clasificación de fallos son, a
/// propósito, casi idénticas para los dos adaptadores.
/// </para>
/// </summary>
public sealed class HandlerHttpFalso : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

    public int Peticiones { get; private set; }

    public HandlerHttpFalso(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _responder = responder;
    }

    /// <summary>Responde siempre con el mismo código, sin cuerpo relevante.</summary>
    public static HandlerHttpFalso ConCodigo(System.Net.HttpStatusCode codigo) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(codigo)
        {
            Content = new StringContent("""{"error":{"message":"fallo simulado"}}""")
        }));

    /// <summary>Lanza la excepción dada en vez de responder, como si la red hubiera fallado.</summary>
    public static HandlerHttpFalso QueLanza(Func<Exception> fabricaExcepcion) =>
        new((_, _) => throw fabricaExcepcion());

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Peticiones++;

        return await _responder(request, cancellationToken);
    }
}
