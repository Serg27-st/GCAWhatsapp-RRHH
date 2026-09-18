using RRHH.WhatsApp.Infrastructure;
using RRHH.WhatsApp.Worker;

// ContentRoot explicito: como servicio de Windows el proceso arranca en System32, y con el directorio
// actual no encontraria appsettings.json ni los archivos que lo acompanan (V26).
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

// Sigue corriendo igual desde la consola: esto solo toma el control cuando lo lanza el administrador
// de servicios, que es como corre en el servidor.
builder.Services.AddWindowsService(opciones => opciones.ServiceName = ServicioWindows.Nombre);

builder.Services.Configure<OpcionesWorker>(builder.Configuration.GetSection(OpcionesWorker.Seccion));

// Exactamente la misma composicion que usa la Api. Es lo que garantiza que una regla se comporte
// igual la dispare un mensaje entrante o un tick del Worker.
builder.Services.AgregarInfraestructura(builder.Configuration);

// V24: un solo Worker procesa. La guardia se registra ANTES que los bucles, y eso es lo que la hace
// funcionar: el host arranca los servicios de a uno y en orden, asi que ningun bucle empieza hasta
// que ella tiene el candado. Por eso se fija el arranque secuencial en vez de confiar en el valor
// por defecto, y sin tope de tiempo: una instancia de reserva puede esperar indefinidamente.
builder.Services.Configure<HostOptions>(opciones =>
{
    opciones.ServicesStartConcurrently = false;
    opciones.StartupTimeout = Timeout.InfiniteTimeSpan;
});

builder.Services.AddSingleton<ICandadoInstancia>(_ => new CandadoSqlServer(
    builder.Configuration.GetConnectionString("RrhhWhatsApp")!, CandadoSqlServer.RecursoWorker));

builder.Services.AddSingleton<GuardiaInstancia>();
builder.Services.AddHostedService(servicios => servicios.GetRequiredService<GuardiaInstancia>());

builder.Services.AddHostedService<ConsumidorOutbox>();
builder.Services.AddHostedService<ServicioBarridoTiempo>();
builder.Services.AddHostedService<ServicioMantenimientoDatos>();
builder.Services.AddHostedService<ServicioReintentoEnvios>();

// V29: lo que decide el bot se encola y sale por aca. Despues de la guardia, como todos los bucles.
builder.Services.AddHostedService<ServicioDespachoEnvios>();

// V33: los archivos de los postulantes se bajan aparte, a su ritmo, antes de que el id caduque.
builder.Services.AddHostedService<ServicioDescargaAdjuntos>();

var host = builder.Build();
var guardia = host.Services.GetRequiredService<GuardiaInstancia>();

try
{
    host.Run();
}
catch (OperationCanceledException)
{
    // Se apago mientras esperaba el candado: nunca llego a procesar, no es una falla.
}

// Perder el candado detiene el proceso a proposito. Salir con codigo distinto de cero es lo que hace
// que el administrador de servicios lo reinicie —la recuperacion la configura
// instalar-servicio-worker.ps1—; al volver, espera su turno como cualquiera.
return guardia.PerdioCandado ? 1 : 0;
