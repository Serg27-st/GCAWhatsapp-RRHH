using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Application.Reglas;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Domain.Reglas;
using RRHH.WhatsApp.Infrastructure;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// Comprueba que el contenedor puede construir el circuito entero. Es una prueba barata que cubre
/// un hueco caro: un servicio implementado pero sin registrar compila igual, y el fallo recien
/// aparece al arrancar el proceso en el servidor.
/// <para>
/// No abre ninguna conexion: <c>UseSqlServer</c> solo configura el proveedor, y la validacion del
/// contenedor resuelve los servicios sin llegar a consultar la base.
/// </para>
/// </summary>
public class RegistroDependenciasTests
{
    private static ServiceProvider Construir()
    {
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:RrhhWhatsApp"] = "Server=(local);Database=Prueba;Trusted_Connection=True"
            })
            .Build();

        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AgregarInfraestructura(configuracion);

        // ValidateOnBuild es lo que convierte esto en una prueba: si a alguna dependencia le falta
        // registro, el contenedor lo dice aca y no en produccion.
        return servicios.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    [Fact]
    public void El_contenedor_resuelve_el_circuito_completo()
    {
        using var proveedor = Construir();
        using var ambito = proveedor.CreateScope();

        var sp = ambito.ServiceProvider;

        Assert.NotNull(sp.GetRequiredService<RecepcionWebhook>());
        Assert.NotNull(sp.GetRequiredService<ProcesadorOutbox>());
        Assert.NotNull(sp.GetRequiredService<BarridoTiempo>());
        Assert.NotNull(sp.GetRequiredService<EvaluadorReglas>());
        Assert.NotNull(sp.GetRequiredService<EjecutorAcciones>());
        Assert.NotNull(sp.GetRequiredService<IFabricaContextoRegla>());
        Assert.NotNull(sp.GetRequiredService<IMotorReglas>());

        // V33: sin esto el bucle del Worker arranca y falla en cada vuelta al pedir el caso de uso.
        Assert.NotNull(sp.GetRequiredService<DescargaAdjuntos>());
    }

    [Theory]
    [InlineData(typeof(IConversacionService))]
    [InlineData(typeof(IMensajeService))]
    [InlineData(typeof(IEventoSistemaService))]
    [InlineData(typeof(IConfiguracionReglasService))]
    [InlineData(typeof(ICuentaService))]
    [InlineData(typeof(IPlantillaService))]
    [InlineData(typeof(IHorarioAtencionService))]
    [InlineData(typeof(IAusenciaService))]
    [InlineData(typeof(IAuditoriaService))]
    [InlineData(typeof(IJobFormsInvitacionService))]
    [InlineData(typeof(IJobFormsService))]
    [InlineData(typeof(IPostulanteService))]
    [InlineData(typeof(IPostulacionService))]
    [InlineData(typeof(IAlmacenamientoCv))]
    [InlineData(typeof(IAlmacenamientoAdjuntos))]
    [InlineData(typeof(IWhatsAppProvider))]
    public void Cada_servicio_de_dominio_que_el_motor_necesita_esta_registrado(Type tipo)
    {
        using var proveedor = Construir();
        using var ambito = proveedor.CreateScope();

        Assert.NotNull(ambito.ServiceProvider.GetRequiredService(tipo));
    }

    [Fact]
    public void El_motor_recibe_todas_las_reglas_implementadas()
    {
        using var proveedor = Construir();
        using var ambito = proveedor.CreateScope();

        var reglas = ambito.ServiceProvider.GetServices<IReglaNegocio>().ToList();

        // Varias reglas del dossier son mas de una clase: la 9 son cuatro (enlace, confirmacion,
        // seguimiento y repregunta), la 2 son dos (escalamiento y segundo nivel, FUN-05) y la 19 son
        // tres (menu, derivacion por silencio y aviso de «Sin clasificar», FUN-06) y la 16 son tres
        // (por postulacion, por hilo y la reactivacion, FUN-11). Responden a
        // disparadores o a momentos distintos, asi que hay mas reglas registradas que codigos.
        Assert.Equal(20, reglas.Count);

        Assert.Equal(
            ["R01", "R02", "R03", "R06", "R08", "R09", "R12", "R14", "R15", "R16", "R19", "R20"],
            reglas.Select(r => r.Codigo).Distinct().OrderBy(c => c).ToArray());
    }

    [Fact]
    public void Ninguna_regla_comparte_prioridad_con_otra_del_mismo_disparador()
    {
        // Dos reglas con la misma prioridad quedan en orden indefinido, y varias dependen de
        // correr antes que otra: la 16 antes de la 2, la 9 antes de la 1.
        using var proveedor = Construir();
        using var ambito = proveedor.CreateScope();

        var prioridades = ambito.ServiceProvider
            .GetServices<IReglaNegocio>()
            .Select(r => r.Prioridad)
            .ToList();

        Assert.Equal(prioridades.Count, prioridades.Distinct().Count());
    }

    /// <summary>Reloj fijo, solo para probar que <c>TryAddSingleton</c> respeta un registro previo.</summary>
    private sealed class RelojFijoDePrueba(DateTimeOffset instante) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instante;
    }

    /// <summary>
    /// ARQ-01: <c>AgregarInfraestructura</c> usa <c>TryAddSingleton</c> para el reloj justamente
    /// para que un host de pruebas (o T0.10, con un <c>FakeTimeProvider</c>) pueda imponer el suyo
    /// antes de cablear la Infrastructure. Si esto se rompe, todos los servicios vuelven a leer el
    /// reloj real sin que ninguna prueba lo note.
    /// </summary>
    [Fact]
    public void Un_reloj_propio_registrado_antes_es_el_que_usan_los_servicios()
    {
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:RrhhWhatsApp"] = "Server=(local);Database=Prueba;Trusted_Connection=True"
            })
            .Build();

        var relojFijo = new RelojFijoDePrueba(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AddSingleton<TimeProvider>(relojFijo);
        servicios.AgregarInfraestructura(configuracion);

        using var proveedor = servicios.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        using var ambito = proveedor.CreateScope();

        Assert.Same(relojFijo, ambito.ServiceProvider.GetRequiredService<TimeProvider>());
    }
}
