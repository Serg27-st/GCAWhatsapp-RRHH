using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Api.Configuracion;
using RRHH.WhatsApp.Api.Controllers;
using RRHH.WhatsApp.Api.Seguridad;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// La particion del limite de velocidad de <c>webhook-google</c> (COR-15/T0.06), probada sin
/// levantar el host: <see cref="LimitesVelocidad.ParticionWebhookGoogle"/> es exactamente la
/// funcion que <c>AddRateLimiter</c> usa en produccion, asi que ejercerla con un
/// <see cref="PartitionedRateLimiter{TResource}"/> propio es fiel a lo que hace el middleware.
/// </summary>
public class LimitesVelocidadTests
{
    private const string Secreto = "el-secreto-compartido";
    private const string Ip = "203.0.113.5";

    private static IServiceProvider ServiciosCon(OpcionesJobForms opciones)
    {
        var servicios = new ServiceCollection();
        servicios.AddSingleton<IOptions<OpcionesJobForms>>(Options.Create(opciones));
        return servicios.BuildServiceProvider();
    }

    private static HttpContext Contexto(IServiceProvider servicios, string? secretoRecibido)
    {
        var contexto = new DefaultHttpContext { RequestServices = servicios };
        contexto.Connection.RemoteIpAddress = IPAddress.Parse(Ip);

        if (secretoRecibido is not null)
            contexto.Request.Headers[SecretoJobForms.Cabecera] = secretoRecibido;

        return contexto;
    }

    private static PartitionedRateLimiter<HttpContext> CrearLimitador() =>
        PartitionedRateLimiter.Create<HttpContext, string>(LimitesVelocidad.ParticionWebhookGoogle);

    /// <summary>
    /// Criterio de "hecho" de T0.06: 100 envios en un minuto desde la misma IP con secreto valido
    /// no reciben 429. El limite por defecto es 600, muy por encima del abuso normal del script.
    /// </summary>
    [Fact]
    public void Cien_envios_con_secreto_valido_desde_la_misma_ip_no_reciben_429()
    {
        var opciones = new OpcionesJobForms
        {
            SecretoWebhook = Secreto,
            LimitePorMinuto = 30,
            LimitePorMinutoWebhook = 600
        };
        var servicios = ServiciosCon(opciones);

        using var limitador = CrearLimitador();

        for (var i = 1; i <= 100; i++)
        {
            using var permiso = limitador.AttemptAcquire(Contexto(servicios, Secreto));

            Assert.True(permiso.IsAcquired, $"La solicitud numero {i} deberia haber sido admitida.");
        }
    }

    /// <summary>
    /// El Apps Script espera lo que diga Retry-After antes de reintentar. El middleware no pone la
    /// cabecera por su cuenta: si <c>OnRejected</c> se pierde, el script reintenta a ciegas.
    /// </summary>
    [Fact]
    public async Task Un_rechazo_por_velocidad_le_dice_al_script_cuanto_esperar()
    {
        var opciones = new OpcionesJobForms { SecretoWebhook = Secreto, LimitePorMinuto = 1 };
        var servicios = ServiciosCon(opciones);

        using var limitador = CrearLimitador();
        using var primero = limitador.AttemptAcquire(Contexto(servicios, null));
        var contexto = Contexto(servicios, null);
        using var rechazado = limitador.AttemptAcquire(contexto);

        Assert.False(rechazado.IsAcquired);

        var registro = new RateLimiterOptions();
        LimitesVelocidad.Configurar(registro, opciones);

        await registro.OnRejected!(new OnRejectedContext { HttpContext = contexto, Lease = rechazado }, default);

        var segundos = int.Parse(contexto.Response.Headers.RetryAfter.ToString());
        Assert.InRange(segundos, 1, 60);
    }

    [Fact]
    public void Sin_secreto_el_intento_que_excede_el_limite_publico_se_rechaza()
    {
        var opciones = new OpcionesJobForms
        {
            SecretoWebhook = Secreto,
            LimitePorMinuto = 5,
            LimitePorMinutoWebhook = 600
        };
        var servicios = ServiciosCon(opciones);

        using var limitador = CrearLimitador();

        for (var i = 1; i <= opciones.LimitePorMinuto; i++)
        {
            using var permiso = limitador.AttemptAcquire(Contexto(servicios, null));

            Assert.True(permiso.IsAcquired, $"La solicitud numero {i} deberia haber sido admitida.");
        }

        using var rechazado = limitador.AttemptAcquire(Contexto(servicios, null));

        Assert.False(rechazado.IsAcquired);
    }

    [Fact]
    public void Un_secreto_invalido_cae_en_el_mismo_cupo_que_la_falta_de_secreto()
    {
        var opciones = new OpcionesJobForms
        {
            SecretoWebhook = Secreto,
            LimitePorMinuto = 2,
            LimitePorMinutoWebhook = 600
        };
        var servicios = ServiciosCon(opciones);

        using var limitador = CrearLimitador();

        using var primero = limitador.AttemptAcquire(Contexto(servicios, "no-coincide"));
        using var segundo = limitador.AttemptAcquire(Contexto(servicios, null));
        using var tercero = limitador.AttemptAcquire(Contexto(servicios, "tampoco-coincide"));

        Assert.True(primero.IsAcquired);
        Assert.True(segundo.IsAcquired);
        // Comparten particion con el intento sin secreto: al tercero ya no le queda cupo.
        Assert.False(tercero.IsAcquired);
    }

    /// <summary>
    /// El corazon de la desviacion respecto de la clave fija que proponia el backlog: un atacante
    /// sin secreto que agota su propio cupo por IP no le quita nada a quien SI trae el secreto
    /// correcto, porque son particiones distintas.
    /// </summary>
    [Fact]
    public void Agotar_el_cupo_sin_secreto_no_afecta_la_particion_del_secreto_valido()
    {
        var opciones = new OpcionesJobForms
        {
            SecretoWebhook = Secreto,
            LimitePorMinuto = 1,
            LimitePorMinutoWebhook = 600
        };
        var servicios = ServiciosCon(opciones);

        using var limitador = CrearLimitador();

        using var agotado = limitador.AttemptAcquire(Contexto(servicios, null));
        Assert.True(agotado.IsAcquired);

        using var rechazadoSinSecreto = limitador.AttemptAcquire(Contexto(servicios, null));
        Assert.False(rechazadoSinSecreto.IsAcquired);

        // Misma IP, ahora con el secreto correcto: otra particion, con su propio cupo intacto.
        using var conSecreto = limitador.AttemptAcquire(Contexto(servicios, Secreto));
        Assert.True(conSecreto.IsAcquired);
    }

    /// <summary>
    /// El controlador entero lleva la politica publica; sin esto la prueba de abajo (que solo mira
    /// la accion) no probaria nada, porque la ausencia de atributo tambien pasaria sin proteccion.
    /// </summary>
    [Fact]
    public void El_controlador_lleva_la_politica_publica_por_defecto()
    {
        var atributo = typeof(JobFormsController)
            .GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: false)
            .Cast<EnableRateLimitingAttribute>()
            .Single();

        Assert.Equal(PoliticasLimite.Publico, atributo.PolicyName);
    }

    /// <summary>
    /// El middleware de rate limiting resuelve la politica por el atributo mas cercano al endpoint:
    /// uno en la accion reemplaza al de la clase. <c>WebhookGoogle</c> tiene que declarar el suyo
    /// propio para no compartir el cupo por IP del resto de los endpoints publicos (COR-15).
    /// </summary>
    [Fact]
    public void WebhookGoogle_declara_su_propia_politica_y_reemplaza_la_de_la_clase()
    {
        var metodo = typeof(JobFormsController).GetMethod(nameof(JobFormsController.WebhookGoogle))!;
        var atributo = metodo.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: false)
            .Cast<EnableRateLimitingAttribute>()
            .Single();

        Assert.Equal(PoliticasLimite.WebhookGoogle, atributo.PolicyName);
    }

    /// <summary>
    /// <c>Estado</c> (GET) y <c>Enviar</c> (camino propio) no declaran politica propia: heredan la
    /// publica de la clase, tal como antes de esta tarea.
    /// </summary>
    [Theory]
    [InlineData(nameof(JobFormsController.Estado))]
    [InlineData(nameof(JobFormsController.Enviar))]
    public void Los_demas_endpoints_no_declaran_politica_propia(string nombreMetodo)
    {
        var metodo = typeof(JobFormsController).GetMethod(nombreMetodo)!;

        Assert.Empty(metodo.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: false));
    }
}
