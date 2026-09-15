using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Infrastructure.Proveedores;

namespace RRHH.WhatsApp.Tests.Proveedores;

/// <summary>
/// Adaptador de la Cloud API de Meta (Sección 9.3, patrón Adapter).
/// <para>
/// Es el camino con número de prueba gratuito. Comparte cuerpos e intérprete con 360dialog —que
/// es un paso a través de esta misma API—, así que lo que hay que probar acá es lo que difiere:
/// la firma del webhook.
/// </para>
/// </summary>
public class MetaCloudProviderTests
{
    private const string Secreto = "secreto-de-la-app";

    private static MetaCloudProvider Proveedor(string appSecret = Secreto) =>
        new(new HttpClient { BaseAddress = new Uri("https://graph.facebook.com/") },
            Options.Create(new OpcionesMetaCloud
            {
                AppSecret = appSecret,
                PhoneNumberId = "123",
                AccessToken = "token"
            }),
            new LimitadorEnvio(10),
            NullLogger<MetaCloudProvider>.Instance);

    /// <summary>Igual que <see cref="Proveedor"/>, pero con un handler falso en vez de red real.</summary>
    private static MetaCloudProvider ProveedorCon(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://graph.facebook.com/") },
            Options.Create(new OpcionesMetaCloud
            {
                AppSecret = Secreto,
                PhoneNumberId = "123",
                AccessToken = "token"
            }),
            new LimitadorEnvio(10),
            NullLogger<MetaCloudProvider>.Instance);

    private static Dictionary<string, string> ConFirma(string cuerpo, string secreto) =>
        new() { [MetaCloudProvider.CabeceraFirma] = "sha256=" + MetaCloudProvider.CalcularFirma(cuerpo, secreto) };

    [Fact]
    public void Una_firma_valida_se_acepta()
    {
        const string cuerpo = """{"object":"whatsapp_business_account"}""";

        Assert.True(Proveedor().ValidarFirma(cuerpo, ConFirma(cuerpo, Secreto)));
    }

    [Fact]
    public void Una_firma_de_otro_secreto_se_rechaza()
    {
        const string cuerpo = """{"object":"whatsapp_business_account"}""";

        Assert.False(Proveedor().ValidarFirma(cuerpo, ConFirma(cuerpo, "secreto-equivocado")));
    }

    [Fact]
    public void Un_cuerpo_alterado_invalida_la_firma()
    {
        // La firma se calcula sobre los bytes exactos: cambiar un caracter la rompe, que es
        // justamente lo que impide que alguien edite el payload en el camino.
        const string original = """{"object":"whatsapp_business_account"}""";

        var cabeceras = ConFirma(original, Secreto);

        Assert.False(Proveedor().ValidarFirma(original + " ", cabeceras));
    }

    [Fact]
    public void Sin_cabecera_de_firma_se_rechaza()
    {
        Assert.False(Proveedor().ValidarFirma("{}", new Dictionary<string, string>()));
    }

    [Fact]
    public void Sin_AppSecret_configurado_se_rechaza_todo()
    {
        // El webhook es publico. Aceptar mensajes que no se pueden atribuir es peor que no
        // recibir ninguno.
        const string cuerpo = "{}";

        Assert.False(Proveedor(appSecret: "").ValidarFirma(cuerpo, ConFirma(cuerpo, Secreto)));
    }

    [Fact]
    public void La_firma_es_hexadecimal_minuscula_como_la_calcula_Meta()
    {
        var firma = MetaCloudProvider.CalcularFirma("hola", Secreto);

        Assert.Equal(64, firma.Length);
        Assert.Equal(firma.ToLowerInvariant(), firma);
    }

    [Fact]
    public void El_interprete_del_webhook_es_el_mismo_que_usa_360dialog()
    {
        // 360dialog proxea la Cloud API, asi que el payload entrante es identico. Si algun dia
        // dejaran de serlo, esta prueba lo muestra.
        var mensajes = Proveedor().InterpretarWebhook(PayloadsDePrueba.MensajeDeTexto);

        Assert.Single(mensajes);
    }

    [Fact]
    public async Task Una_plantilla_sin_aprobar_no_sale_a_la_red()
    {
        var plantilla = new RRHH.WhatsApp.Domain.Entidades.Plantilla
        {
            Clave = "prueba", NombreMeta = "prueba", TextoAprobado = "hola",
            CantidadParametros = 0, Activa = false
        };

        var resultado = await Proveedor().EnviarPlantillaAsync("+51987654321", plantilla, []);

        Assert.False(resultado.Exito);
        Assert.Contains("no esta activa", resultado.Error);
    }

    // ---------------------------------------------------------------------------------------
    // T0.03 (ARQ-04/C4): sin `.AddStandardResilienceHandler()`, la clasificación de la falla la
    // hace el propio adaptador. Estas pruebas fijan ese contrato con un handler falso, para que
    // una reintroducción del handler de resiliencia (que reintenta solo y esquiva el
    // LimitadorEnvio, el patrón de la Sección 9.6.4 que costó la línea) rompa algo aquí.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Un_503_produce_una_sola_peticion_y_resultado_transitorio()
    {
        var handler = HandlerHttpFalso.ConCodigo(HttpStatusCode.ServiceUnavailable);
        var proveedor = ProveedorCon(handler);

        var resultado = await proveedor.EnviarTextoAsync("+51987654321", "hola");

        // Con el handler de resiliencia reinstalado, esto pediría 3+ veces: es justo lo que
        // ARQ-04 prohíbe, porque esos reintentos saltan el limitador y duplican al postulante.
        Assert.Equal(1, handler.Peticiones);
        Assert.False(resultado.Exito);
        Assert.Equal(ClaseFallo.Transitorio, resultado.Clase);
    }

    [Fact]
    public async Task Una_excepcion_no_http_tras_enviar_da_ambiguo_sin_propagar()
    {
        // Cualquier falla que no sea "la conexion nunca salio" pudo ocurrir despues de que el
        // mensaje ya viajo a Meta: no es seguro reintentarla sola (V29).
        var handler = HandlerHttpFalso.QueLanza(() => new IOException("el socket se cerro solo"));
        var proveedor = ProveedorCon(handler);

        var resultado = await proveedor.EnviarTextoAsync("+51987654321", "hola");

        Assert.False(resultado.Exito);
        Assert.Equal(ClaseFallo.Ambiguo, resultado.Clase);
    }

    [Fact]
    public async Task Una_falla_de_conexion_que_no_llego_a_salir_da_transitorio()
    {
        // NameResolutionError, ConnectionError y SecureConnectionError son los tres casos en que
        // Meta nunca vio la peticion: ahi si es seguro reintentar.
        var handler = HandlerHttpFalso.QueLanza(() =>
            new HttpRequestException(HttpRequestError.ConnectionError, "conexion rechazada"));
        var proveedor = ProveedorCon(handler);

        var resultado = await proveedor.EnviarTextoAsync("+51987654321", "hola");

        Assert.False(resultado.Exito);
        Assert.Equal(ClaseFallo.Transitorio, resultado.Clase);
    }

    [Fact]
    public async Task Una_falla_http_que_pudo_haber_salido_da_ambiguo()
    {
        // ResponseEnded: la peticion sí viajó y se perdió la respuesta. Distinto de una falla de
        // conexión, y por eso no puede clasificar igual.
        var handler = HandlerHttpFalso.QueLanza(() =>
            new HttpRequestException(HttpRequestError.ResponseEnded, "se corto la respuesta"));
        var proveedor = ProveedorCon(handler);

        var resultado = await proveedor.EnviarTextoAsync("+51987654321", "hola");

        Assert.False(resultado.Exito);
        Assert.Equal(ClaseFallo.Ambiguo, resultado.Clase);
    }

    [Fact]
    public async Task La_cancelacion_del_llamador_se_propaga_sin_convertirse_en_resultado()
    {
        // Distinto de una falla del proveedor: esto lo pidió quien llamó, no Meta. No debe
        // convertirse en ResultadoEnvio.Ambiguo ni en ningun otro resultado.
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
