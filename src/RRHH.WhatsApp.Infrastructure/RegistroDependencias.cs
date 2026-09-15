using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Application.Reglas;
using RRHH.WhatsApp.Application.Reglas.Implementaciones;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Domain.Reglas;
using RRHH.WhatsApp.Infrastructure.Almacenamiento;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Proveedores;
using RRHH.WhatsApp.Infrastructure.Servicios;

namespace RRHH.WhatsApp.Infrastructure;

public static class RegistroDependencias
{
    /// <summary>
    /// Cablea persistencia, servicios de dominio, motor de reglas y proveedor. La usan tanto la Api
    /// como el Worker, para que ambos vean exactamente la misma composicion.
    /// </summary>
    public static IServiceCollection AgregarInfraestructura(
        this IServiceCollection servicios, IConfiguration configuracion)
    {
        var cadena = configuracion.GetConnectionString("RrhhWhatsApp")
            ?? throw new InvalidOperationException(
                "Falta la cadena de conexion 'RrhhWhatsApp'. Debe venir de variable de entorno por ambiente.");

        servicios.AddDbContext<RrhhDbContext>(opciones => opciones.UseSqlServer(cadena));
        servicios.AddMemoryCache();

        servicios.Configure<OpcionesCv>(configuracion.GetSection(OpcionesCv.Seccion));
        servicios.Configure<OpcionesAntivirus>(configuracion.GetSection(OpcionesAntivirus.Seccion));
        servicios.AddScoped<IAlmacenamientoCv, AlmacenamientoCvLocal>();

        // Seccion 9.6.1: el CV pasa por el antivirus antes de guardarse. Apagarlo es una decision
        // explicita —el escaner nulo avisa en cada archivo— y no un descuido de configuracion.
        var escanearCv = configuracion
            .GetSection(OpcionesAntivirus.Seccion)
            .GetValue("Habilitado", true);

        if (escanearCv)
            servicios.AddScoped<IEscanerAntivirus, EscanerDefender>();
        else
            servicios.AddScoped<IEscanerAntivirus, EscanerDesactivado>();

        // Servicios de dominio. Cada uno es la frontera de su modulo: el resto del sistema pasa
        // por su interfaz y nunca toca las tablas del otro (desviacion V6 de docs/decisiones.md).
        servicios.AddScoped<IConversacionService, ConversacionService>();
        servicios.AddScoped<IMensajeService, MensajeService>();
        servicios.AddScoped<IEventoSistemaService, EventoSistemaService>();
        servicios.AddScoped<IConfiguracionReglasService, ConfiguracionReglasService>();
        servicios.AddScoped<ICuentaService, CuentaService>();
        servicios.AddScoped<IPlantillaService, PlantillaService>();
        servicios.AddScoped<IHorarioAtencionService, HorarioAtencionService>();
        servicios.AddScoped<IAusenciaService, AusenciaService>();
        servicios.AddScoped<IAuditoriaService, AuditoriaService>();
        servicios.AddScoped<IAnalistaService, AnalistaService>();
        servicios.AddScoped<IAutenticacionService, AutenticacionService>();
        servicios.AddScoped<ILatidoServicio, LatidoServicioService>();
        servicios.AddScoped<IJobFormsInvitacionService, JobFormsInvitacionService>();
        servicios.AddScoped<IJobFormsService, JobFormsService>();
        servicios.AddScoped<IPostulanteService, PostulanteService>();
        servicios.AddScoped<IPostulacionService, PostulacionService>();

        servicios.AgregarReglas();
        servicios.AgregarProveedorWhatsApp(configuracion);

        // El circuito completo: el webhook encola, el procesador arma el contexto, el motor decide
        // y el ejecutor produce los efectos. Sin estos registros el motor queda escrito pero
        // sin nadie que lo dispare, y la outbox se llena sin consumidor.
        servicios.AddScoped<IFabricaContextoRegla, FabricaContextoRegla>();
        servicios.AddScoped<EjecutorAcciones>();
        servicios.AddScoped<EvaluadorReglas>();
        servicios.AddScoped<RecepcionWebhook>();
        servicios.AddScoped<ReintentoEnvios>();
        servicios.AddScoped<RecepcionJobForms>();
        servicios.AddScoped<EnvioAnalista>();
        servicios.AddScoped<AccionesBandeja>();
        servicios.AddScoped<ProcesadorOutbox>();
        servicios.AddScoped<BarridoTiempo>();

        return servicios;
    }

    /// <summary>
    /// Cada regla se registra por separado y el motor las recibe todas. Agregar una regla nueva es
    /// agregar una linea aca, sin tocar el motor ni las demas (patron Strategy).
    /// </summary>
    public static IServiceCollection AgregarReglas(this IServiceCollection servicios)
    {
        servicios.AddScoped<IReglaNegocio, R15OptInYVentana>();
        servicios.AddScoped<IReglaNegocio, R19FallbackMenu>();
        servicios.AddScoped<IReglaNegocio, R09RepreguntaEmpresa>();
        servicios.AddScoped<IReglaNegocio, R14Ausencias>();
        servicios.AddScoped<IReglaNegocio, R16Archivado>();
        servicios.AddScoped<IReglaNegocio, R02Escalamiento>();
        servicios.AddScoped<IReglaNegocio, R01Asignacion>();
        servicios.AddScoped<IReglaNegocio, R20VacanteCerrada>();
        servicios.AddScoped<IReglaNegocio, R09EnvioLink>();
        servicios.AddScoped<IReglaNegocio, R09ConfirmacionJobForms>();
        servicios.AddScoped<IReglaNegocio, R09SeguimientoJobForms>();
        servicios.AddScoped<IReglaNegocio, R03FueraDeHorario>();
        servicios.AddScoped<IReglaNegocio, R12CierreCortesia>();

        servicios.AddScoped<IMotorReglas, MotorReglas>();

        return servicios;
    }

    /// <summary>
    /// Elige contra qué habla el sistema. El orden es deliberado: Meta Cloud API primero, porque
    /// es el que tiene número de prueba gratuito y sirve para validar el flujo antes de contratar
    /// el BSP; 360dialog después (decisión D2); y el simulado si no hay nada configurado.
    /// </summary>
    private static IServiceCollection AgregarProveedorWhatsApp(
        this IServiceCollection servicios, IConfiguration configuracion)
    {
        var seccionMeta = configuracion.GetSection(OpcionesMetaCloud.Seccion);
        var seccion360 = configuracion.GetSection(Dialog360Opciones.Seccion);

        servicios.Configure<OpcionesMetaCloud>(seccionMeta);
        servicios.Configure<Dialog360Opciones>(seccion360);

        var meta = seccionMeta.Get<OpcionesMetaCloud>() ?? new OpcionesMetaCloud();
        var dialog = seccion360.Get<Dialog360Opciones>() ?? new Dialog360Opciones();

        // T0.08 registrara TimeProvider formalmente para toda la Infrastructure; TryAdd evita
        // pisarlo si ese registro ya corrio (o corre despues) en el mismo contenedor.
        servicios.TryAddSingleton(TimeProvider.System);

        // Respaldo de appsettings para cuando envio.maximo_por_segundo no se puede leer de la
        // base (COR-12/AL8): el de quien haya quedado configurado, Meta o 360dialog.
        var maximoPorSegundoDeRespaldo = meta.EstaConfigurado ? meta.MaximoPorSegundo : dialog.MaximoPorSegundo;

        // El limitador y su proveedor de parametros se registran una sola vez, antes de elegir
        // proveedor, para no duplicar este cableado en las tres ramas de mas abajo (Meta /
        // 360dialog / simulado). ProveedorParametrosEnvio hace lo que antes hacia leer
        // meta.MaximoPorSegundo o dialog.MaximoPorSegundo una vez al arrancar: ahora consulta
        // envio.maximo_por_segundo en cada turno, con cache de 30 s, y cae a este respaldo si la
        // base no tiene el parametro o no responde.
        servicios.AddSingleton(sp => new ProveedorParametrosEnvio(
            sp.GetRequiredService<IServiceScopeFactory>(),
            maximoPorSegundoDeRespaldo,
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<ProveedorParametrosEnvio>>()));

        servicios.AddSingleton(sp => new LimitadorEnvio(
            ct => sp.GetRequiredService<ProveedorParametrosEnvio>().ObtenerMaximoPorSegundoAsync(ct)));

        if (meta.EstaConfigurado)
        {
            // Sin handler de resiliencia (ARQ-04/C4, V29): reintentar un POST por su cuenta anula
            // "Ambiguo no se reintenta solo", esquiva el LimitadorEnvio y se suma a los reintentos
            // de ReintentoEnvios. El unico reintento del sistema es ese; el adaptador clasifica la
            // excepcion y no reintenta nada.
            servicios.AddHttpClient<IWhatsAppProvider, MetaCloudProvider>(cliente =>
            {
                cliente.BaseAddress = new Uri("https://graph.facebook.com/");
                cliente.Timeout = TimeSpan.FromSeconds(meta.TimeoutSegundos);
                cliente.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", meta.AccessToken);
            });

            return servicios;
        }

        // Sin credenciales de ninguno se trabaja contra el proveedor simulado. Es lo que permite
        // avanzar mientras el WABA sigue en aprobacion, que es el cuello de botella real.
        if (!dialog.EstaConfigurado)
        {
            servicios.AddScoped<IWhatsAppProvider>(sp => new ProveedorSimulado(
                sp.GetRequiredService<ILogger<ProveedorSimulado>>(),
                dialog.SecretoWebhook));

            return servicios;
        }

        // Misma razon que en el cliente de Meta: sin handler de resiliencia (ARQ-04/C4, V29).
        servicios.AddHttpClient<IWhatsAppProvider, Dialog360Provider>(cliente =>
        {
            cliente.BaseAddress = new Uri(dialog.BaseUrl.TrimEnd('/') + "/");
            cliente.Timeout = TimeSpan.FromSeconds(dialog.TimeoutSegundos);
            cliente.DefaultRequestHeaders.Add("D360-API-KEY", dialog.ApiKey);
        });

        return servicios;
    }
}
