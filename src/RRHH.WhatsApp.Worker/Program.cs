using RRHH.WhatsApp.Infrastructure;
using RRHH.WhatsApp.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<OpcionesWorker>(builder.Configuration.GetSection(OpcionesWorker.Seccion));

// Exactamente la misma composicion que usa la Api. Es lo que garantiza que una regla se comporte
// igual la dispare un mensaje entrante o un tick del Worker.
builder.Services.AgregarInfraestructura(builder.Configuration);

builder.Services.AddHostedService<ConsumidorOutbox>();
builder.Services.AddHostedService<ServicioBarridoTiempo>();
builder.Services.AddHostedService<ServicioPurgaCv>();
builder.Services.AddHostedService<ServicioReintentoEnvios>();

var host = builder.Build();
host.Run();
