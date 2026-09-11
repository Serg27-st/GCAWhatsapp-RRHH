using RRHH.WhatsApp.Frontend.Components;
using RRHH.WhatsApp.Frontend.Servicios;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<OpcionesApi>(builder.Configuration.GetSection(OpcionesApi.Seccion));

var baseUrl = builder.Configuration.GetSection(OpcionesApi.Seccion)
    .GetValue("BaseUrl", "http://localhost:5087")!;

// El Frontend solo habla HTTP con la Api: no referencia el dominio ni toca SQL Server.
builder.Services.AddHttpClient<ClienteApi>(cliente =>
{
    cliente.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    cliente.Timeout = TimeSpan.FromSeconds(30);
});

// Por circuito de Blazor: cada analista con su sesion. Es provisional hasta que haya login.
builder.Services.AddScoped<SesionAnalista>();

// Uno por circuito: cada analista escucha su propio grupo del hub (Seccion 9.6.3).
builder.Services.AddScoped<CanalEnVivo>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
