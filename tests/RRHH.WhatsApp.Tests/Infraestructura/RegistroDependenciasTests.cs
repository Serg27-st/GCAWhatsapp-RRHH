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

        // La Regla 9 son cuatro clases: envio del enlace, confirmacion, seguimiento y repregunta.
        // Responden a disparadores distintos, asi que hay mas reglas registradas que codigos.
        Assert.Equal(13, reglas.Count);

        Assert.Equal(
            ["R01", "R02", "R03", "R09", "R12", "R14", "R15", "R16", "R19", "R20"],
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
}
