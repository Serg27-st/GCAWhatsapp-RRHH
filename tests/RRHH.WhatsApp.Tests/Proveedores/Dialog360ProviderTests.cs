using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Infrastructure.Proveedores;

namespace RRHH.WhatsApp.Tests.Proveedores;

/// <summary>
/// T0.03 (ARQ-04/C4): mismas pruebas de clasificación de fallos que
/// <see cref="MetaCloudProviderTests"/>, porque 360dialog es un paso a través de la misma Cloud
/// API y comparte el mismo bloque <c>catch</c> en su <c>EnviarAsync</c>. Sin
/// <c>.AddStandardResilienceHandler()</c>, es este adaptador el que tiene que clasificar bien o
/// nadie lo hace.
/// </summary>
public class Dialog360ProviderTests
{
    private static Dialog360Provider ProveedorCon(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://waba-v2.360dialog.io/") },
            Options.Create(new Dialog360Opciones { ApiKey = "clave-prueba" }),
            new LimitadorEnvio(10),
            TimeProvider.System,
            NullLogger<Dialog360Provider>.Instance);

    [Fact]
    public async Task Un_503_produce_una_sola_peticion_y_resultado_transitorio()
    {
        var handler = HandlerHttpFalso.ConCodigo(HttpStatusCode.ServiceUnavailable);
        var proveedor = ProveedorCon(handler);

        var resultado = await proveedor.EnviarTextoAsync("+51987654321", "hola");

        Assert.Equal(1, handler.Peticiones);
        Assert.False(resultado.Exito);
        Assert.Equal(ClaseFallo.Transitorio, resultado.Clase);
    }

    [Fact]
    public async Task Una_excepcion_no_http_tras_enviar_da_ambiguo_sin_propagar()
    {
        var handler = HandlerHttpFalso.QueLanza(() => new IOException("el socket se cerro solo"));
        var proveedor = ProveedorCon(handler);

        var resultado = await proveedor.EnviarTextoAsync("+51987654321", "hola");

        Assert.False(resultado.Exito);
        Assert.Equal(ClaseFallo.Ambiguo, resultado.Clase);
    }

    [Fact]
    public async Task Una_falla_de_conexion_que_no_llego_a_salir_da_transitorio()
    {
        var handler = HandlerHttpFalso.QueLanza(() =>
            new HttpRequestException(HttpRequestError.NameResolutionError, "no resuelve el host"));
        var proveedor = ProveedorCon(handler);

        var resultado = await proveedor.EnviarTextoAsync("+51987654321", "hola");

        Assert.False(resultado.Exito);
        Assert.Equal(ClaseFallo.Transitorio, resultado.Clase);
    }

    [Fact]
    public async Task Una_falla_http_que_pudo_haber_salido_da_ambiguo()
    {
        var handler = HandlerHttpFalso.QueLanza(() =>
            new HttpRequestException(HttpRequestError.InvalidResponse, "respuesta invalida"));
        var proveedor = ProveedorCon(handler);

        var resultado = await proveedor.EnviarTextoAsync("+51987654321", "hola");

        Assert.False(resultado.Exito);
        Assert.Equal(ClaseFallo.Ambiguo, resultado.Clase);
    }

    [Fact]
    public async Task La_cancelacion_del_llamador_se_propaga_sin_convertirse_en_resultado()
    {
        using var cts = new CancellationTokenSource();

        var handler = HandlerHttpFalso.QueLanza(() =>
        {
            cts.Cancel();
            return new OperationCanceledException(cts.Token);
        });

        var proveedor = ProveedorCon(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => proveedor.EnviarTextoAsync("+51987654321", "hola", cts.Token));
    }
}
