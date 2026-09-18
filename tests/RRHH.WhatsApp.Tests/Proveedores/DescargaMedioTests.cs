using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Proveedores;

namespace RRHH.WhatsApp.Tests.Proveedores;

/// <summary>
/// T5.03 (ARQ-10, V33): bajar el archivo que mandó el postulante. Son dos pasos en la Cloud API
/// —pedir la URL del medio y bajarla con credenciales— y la URL vale unos minutos.
/// <para>
/// A diferencia de un envío, pedir un archivo de nuevo no duplica nada: no hay fallo ambiguo. Lo que
/// importa es separar lo que vale la pena reintentar de lo que no.
/// </para>
/// </summary>
public class DescargaMedioTests
{
    private const string IdMedio = "1037543291543636";
    private const string Token = "token-de-prueba";
    private const string ClaveApi = "clave-360";

    private const string RutaCdn = "/whatsapp_business/attachments/?mid=1037543291543636&ext=1726600000&hash=ATsxQk";

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4");

    /// <summary>
    /// Como la devuelve la Graph API: las barras vienen escapadas, que es lo que la documentación de
    /// 360dialog llama «quitar las barras invertidas». Un lector de JSON las resuelve solo.
    /// </summary>
    private static string Metadatos(string url = "https://lookaside.fbsbx.com" + RutaCdn) => $$"""
        {
          "url": "{{url.Replace("/", "\\/")}}",
          "mime_type": "application/pdf",
          "sha256": "0c7ce1b5a2c4b8d7",
          "file_size": {{Pdf.Length}},
          "id": "{{IdMedio}}",
          "messaging_product": "whatsapp"
        }
        """;

    private static HttpResponseMessage Json(string cuerpo) =>
        new(HttpStatusCode.OK) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Archivo()
    {
        var contenido = new ByteArrayContent(Pdf);
        contenido.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = contenido };
    }

    private static HttpResponseMessage Error(HttpStatusCode codigo) =>
        new(codigo) { Content = new StringContent("""{"error":{"message":"fallo simulado"}}""") };

    /// <summary>Lo que el adaptador pidió, con lo que llevaba: la URL de descarga no puede ir sin credenciales.</summary>
    private sealed record Peticion(Uri Url, string? Autorizacion, string? ClaveApi);

    private static (HandlerHttpFalso Handler, List<Peticion> Peticiones) Red(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var peticiones = new List<Peticion>();

        var handler = new HandlerHttpFalso((pedido, _) =>
        {
            peticiones.Add(new Peticion(
                pedido.RequestUri!,
                pedido.Headers.Authorization?.ToString(),
                pedido.Headers.TryGetValues("D360-API-KEY", out var claves) ? claves.Single() : null));

            return Task.FromResult(responder(pedido));
        });

        return (handler, peticiones);
    }

    /// <summary>Como lo arma <c>RegistroDependencias</c>: el token va como cabecera por defecto del cliente.</summary>
    private static MetaCloudProvider Meta(HttpMessageHandler handler, LimitadorEnvio? limitador = null)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://graph.facebook.com/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        return new MetaCloudProvider(
            http,
            Options.Create(new OpcionesMetaCloud { AccessToken = Token, PhoneNumberId = "123", AppSecret = "s" }),
            limitador ?? new LimitadorEnvio(10),
            TimeProvider.System,
            NullLogger<MetaCloudProvider>.Instance);
    }

    private static Dialog360Provider Dialog360(HttpMessageHandler handler, string claveApi = ClaveApi)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://waba-v2.360dialog.io/") };
        http.DefaultRequestHeaders.Add("D360-API-KEY", claveApi);

        return new Dialog360Provider(
            http,
            Options.Create(new Dialog360Opciones { ApiKey = claveApi }),
            new LimitadorEnvio(10),
            TimeProvider.System,
            NullLogger<Dialog360Provider>.Instance);
    }

    private static async Task<byte[]> LeerAsync(ResultadoDescarga resultado)
    {
        await using var medio = Assert.IsType<MedioDescargado>(resultado.Medio);
        using var copia = new MemoryStream();

        await medio.Contenido.CopyToAsync(copia);

        return copia.ToArray();
    }

    [Fact]
    public async Task Meta_baja_el_archivo_en_dos_pasos_con_el_token()
    {
        var (handler, peticiones) = Red(p => p.RequestUri!.Host == "graph.facebook.com" ? Json(Metadatos()) : Archivo());

        var resultado = await Meta(handler).DescargarMedioAsync(IdMedio);

        Assert.True(resultado.Exito);
        Assert.Equal("application/pdf", resultado.Medio!.MimeType);
        Assert.Equal(Pdf.Length, resultado.Medio.Tamano);
        Assert.Equal(Pdf, await LeerAsync(resultado));

        Assert.Collection(peticiones,
            p => Assert.Equal($"https://graph.facebook.com/v25.0/{IdMedio}", p.Url.AbsoluteUri),
            p =>
            {
                Assert.Equal("https://lookaside.fbsbx.com" + RutaCdn, p.Url.AbsoluteUri);
                Assert.Equal($"Bearer {Token}", p.Autorizacion);
            });
    }

    /// <summary>El id vencido o inexistente no cambia reintentando: se da por perdido.</summary>
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Un_rechazo_del_proveedor_es_permanente(HttpStatusCode codigo)
    {
        var (handler, peticiones) = Red(_ => Error(codigo));

        var resultado = await Meta(handler).DescargarMedioAsync(IdMedio);

        Assert.False(resultado.Exito);
        Assert.Equal(ClaseFallo.Permanente, resultado.Clase);
        Assert.Contains(((int)codigo).ToString(), resultado.Error);
        Assert.Single(peticiones);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task Un_proveedor_caido_o_saturado_es_transitorio(HttpStatusCode codigo)
    {
        var (handler, _) = Red(_ => Error(codigo));

        var resultado = await Meta(handler).DescargarMedioAsync(IdMedio);

        Assert.Equal(ClaseFallo.Transitorio, resultado.Clase);
    }

    /// <summary>La URL ya se obtuvo, pero el archivo no bajó: pedir todo de nuevo es seguro.</summary>
    [Fact]
    public async Task Si_falla_la_bajada_del_archivo_es_transitorio()
    {
        var (handler, peticiones) = Red(p =>
            p.RequestUri!.Host == "graph.facebook.com" ? Json(Metadatos()) : Error(HttpStatusCode.BadGateway));

        var resultado = await Meta(handler).DescargarMedioAsync(IdMedio);

        Assert.Equal(ClaseFallo.Transitorio, resultado.Clase);
        Assert.Equal(2, peticiones.Count);
    }

    /// <summary>
    /// Un GET no cambia nada del lado del proveedor: aunque la conexión se corte a mitad de la
    /// respuesta, repetirlo no duplica nada. Por eso acá no existe el fallo ambiguo de los envíos.
    /// </summary>
    [Fact]
    public async Task Una_red_que_falla_es_transitoria()
    {
        var handler = HandlerHttpFalso.QueLanza(() =>
            new HttpRequestException(HttpRequestError.ResponseEnded, "se corto la respuesta"));

        var resultado = await Meta(handler).DescargarMedioAsync(IdMedio);

        Assert.Equal(ClaseFallo.Transitorio, resultado.Clase);
    }

    [Fact]
    public async Task Un_tiempo_agotado_es_transitorio()
    {
        var handler = HandlerHttpFalso.QueLanza(() => new TaskCanceledException("sin respuesta"));

        var resultado = await Meta(handler).DescargarMedioAsync(IdMedio);

        Assert.Equal(ClaseFallo.Transitorio, resultado.Clase);
    }

    /// <summary>El token no viaja por una conexión sin cifrar, aunque la URL venga de la respuesta de Meta.</summary>
    [Fact]
    public async Task Una_url_sin_https_no_se_pide()
    {
        var (handler, peticiones) = Red(_ => Json(Metadatos("http://lookaside.fbsbx.com" + RutaCdn)));

        var resultado = await Meta(handler).DescargarMedioAsync(IdMedio);

        Assert.Equal(ClaseFallo.Permanente, resultado.Clase);
        Assert.Single(peticiones);
    }

    [Fact]
    public async Task Una_respuesta_sin_url_es_permanente()
    {
        var (handler, peticiones) = Red(_ => Json("""{ "id": "1037543291543636" }"""));

        var resultado = await Meta(handler).DescargarMedioAsync(IdMedio);

        Assert.Equal(ClaseFallo.Permanente, resultado.Clase);
        Assert.Single(peticiones);
    }

    /// <summary>
    /// T5.03: bajar un archivo no es un mensaje saliente ni cuenta para el patrón que provocó el
    /// bloqueo. Con un limitador que nunca da turno, la descarga termina igual.
    /// </summary>
    [Fact]
    public async Task La_descarga_no_espera_turno_en_el_limitador_de_envio()
    {
        var nuncaDaTurno = new LimitadorEnvio(_ => new ValueTask<int>(new TaskCompletionSource<int>().Task));
        var (handler, _) = Red(p => p.RequestUri!.Host == "graph.facebook.com" ? Json(Metadatos()) : Archivo());

        var resultado = await Meta(handler, nuncaDaTurno)
            .DescargarMedioAsync(IdMedio)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(resultado.Exito);
        await LeerAsync(resultado);
    }

    /// <summary>
    /// 360dialog devuelve la misma URL de Meta, pero el archivo se pide a su propio host con su clave:
    /// el token de Meta no lo tenemos.
    /// </summary>
    [Fact]
    public async Task Dialog360_pide_el_archivo_a_su_host_con_su_clave()
    {
        var (handler, peticiones) = Red(p => p.RequestUri!.AbsolutePath == $"/{IdMedio}" ? Json(Metadatos()) : Archivo());

        var resultado = await Dialog360(handler).DescargarMedioAsync(IdMedio);

        Assert.True(resultado.Exito);
        Assert.Equal(Pdf, await LeerAsync(resultado));

        Assert.Collection(peticiones,
            p =>
            {
                Assert.Equal($"https://waba-v2.360dialog.io/{IdMedio}", p.Url.AbsoluteUri);
                Assert.Equal(ClaveApi, p.ClaveApi);
            },
            p =>
            {
                Assert.Equal("https://waba-v2.360dialog.io" + RutaCdn, p.Url.AbsoluteUri);
                Assert.Equal(ClaveApi, p.ClaveApi);
            });
    }

    [Fact]
    public async Task Dialog360_sin_clave_no_sale_a_la_red()
    {
        var (handler, peticiones) = Red(_ => Json(Metadatos()));

        var resultado = await Dialog360(handler, claveApi: "").DescargarMedioAsync(IdMedio);

        Assert.Equal(ClaseFallo.Permanente, resultado.Clase);
        Assert.Empty(peticiones);
    }

    [Fact]
    public async Task Dialog360_caido_es_transitorio()
    {
        var (handler, _) = Red(_ => Error(HttpStatusCode.ServiceUnavailable));

        var resultado = await Dialog360(handler).DescargarMedioAsync(IdMedio);

        Assert.Equal(ClaseFallo.Transitorio, resultado.Clase);
    }

    /// <summary>En desarrollo no hay Meta: el simulado entrega siempre el mismo archivo, para recorrer el circuito.</summary>
    [Fact]
    public async Task El_simulado_entrega_un_archivo_fijo()
    {
        var simulado = new ProveedorSimulado(TimeProvider.System, NullLogger<ProveedorSimulado>.Instance);

        var resultado = await simulado.DescargarMedioAsync(IdMedio);

        Assert.True(resultado.Exito);
        Assert.NotEmpty(await LeerAsync(resultado));
        Assert.Contains(IdMedio, simulado.MediosDescargados);
    }
}
