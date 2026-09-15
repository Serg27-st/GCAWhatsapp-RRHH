using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure;

namespace RRHH.WhatsApp.Tests.Proveedores;

/// <summary>
/// T0.03 (ARQ-04/C4): a diferencia de <see cref="MetaCloudProviderTests"/>, que construye el
/// proveedor a mano, esta prueba resuelve <see cref="IWhatsAppProvider"/> desde el contenedor
/// real —el mismo <see cref="RegistroDependencias.AgregarInfraestructura"/> que usan la Api y el
/// Worker— para que una reintroducción de <c>.AddStandardResilienceHandler()</c> en
/// <c>RegistroDependencias.cs</c> rompa esto, no solo las pruebas que construyen el proveedor
/// directamente.
/// <para>
/// El truco es reemplazar el <see cref="HttpMessageHandler"/> primario del cliente tipado
/// después de registrar la infraestructura completa: <c>AddHttpClient&lt;TClient, TImpl&gt;</c>
/// nombra internamente al cliente como <c>typeof(TClient).Name</c>, así que
/// <c>Configure&lt;HttpClientFactoryOptions&gt;(nameof(IWhatsAppProvider), ...)</c> apunta al
/// mismo cliente que arma <c>RegistroDependencias</c>. Si el handler de resiliencia sigue ahí,
/// sigue envolviendo a nuestro handler falso igual que envolvería al real.
/// </para>
/// </summary>
public class ComposicionProveedorTests
{
    private static ServiceProvider Construir(HandlerHttpFalso handlerFalso)
    {
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:RrhhWhatsApp"] = "Server=(local);Database=Prueba;Trusted_Connection=True",
                ["Cv:Antivirus:Habilitado"] = "false",

                // Con esto configurado, AgregarProveedorWhatsApp elige MetaCloudProvider (es el
                // primero en el orden de la fabrica): src/.../RegistroDependencias.cs.
                ["MetaCloud:AccessToken"] = "token-de-prueba",
                ["MetaCloud:PhoneNumberId"] = "123",
                ["MetaCloud:AppSecret"] = "secreto-de-prueba",
            })
            .Build();

        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AgregarInfraestructura(configuracion);

        // Sustituye el handler real por el falso sin tocar el resto de la cadena: si alguien
        // reinstala AddStandardResilienceHandler(), ese handler sigue envolviendo a este.
        servicios.Configure<HttpClientFactoryOptions>(nameof(IWhatsAppProvider), opciones =>
            opciones.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = handlerFalso));

        return servicios.BuildServiceProvider();
    }

    [Fact]
    public async Task El_cliente_compuesto_por_RegistroDependencias_no_reintenta_un_503()
    {
        var handlerFalso = HandlerHttpFalso.ConCodigo(HttpStatusCode.ServiceUnavailable);

        using var proveedorServicios = Construir(handlerFalso);
        using var ambito = proveedorServicios.CreateScope();

        var proveedor = ambito.ServiceProvider.GetRequiredService<IWhatsAppProvider>();

        Assert.Equal("Meta Cloud API", proveedor.Nombre);

        var resultado = await proveedor.EnviarTextoAsync("+51987654321", "hola");

        // El numero que importa: con AddStandardResilienceHandler() de vuelta, esto seria 3 o mas
        // (el handler estandar reintenta 5xx) y el resultado seguiria clasificando Transitorio,
        // asi que solo el conteo de peticiones detecta la regresion.
        Assert.Equal(1, handlerFalso.Peticiones);
        Assert.False(resultado.Exito);
        Assert.Equal(ClaseFallo.Transitorio, resultado.Clase);
    }
}
