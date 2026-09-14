using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using RRHH.WhatsApp.Api.Seguridad;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RRHH.WhatsApp.Api.Configuracion;
using RRHH.WhatsApp.Api.Salud;
using RRHH.WhatsApp.Api.TiempoReal;
using RRHH.WhatsApp.Contracts.TiempoReal;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure;
using RRHH.WhatsApp.Reporting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddOpenApi();


// Seccion 9.6.1. Todo endpoint exige token salvo los que se marcan [AllowAnonymous]: el webhook de
// WhatsApp, el circuito publico del JobForms, el login y la salud. Es al reves de lo habitual a
// proposito — olvidarse de proteger un endpoint nuevo deberia romperlo, no exponerlo.
var jwt = builder.Configuration.GetSection(OpcionesJwt.Seccion).Get<OpcionesJwt>() ?? new OpcionesJwt();


// Sin clave no se arranca. La alternativa —firmar con un relleno -- daria tokens que cualquiera
// puede falsificar, y el sistema andaria igual: es la clase de fallo que nadie nota hasta que
// alguien lo aprovecha.
if (!jwt.EstaConfigurada)
{
    throw new InvalidOperationException(
        "Falta Jwt:Clave (minimo 32 caracteres). Debe llegar por la variable de entorno Jwt__Clave.");
}
builder.Services.Configure<OpcionesJwt>(builder.Configuration.GetSection(OpcionesJwt.Seccion));
builder.Services.AddSingleton<EmisorTokens>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opciones =>
    {
        opciones.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Emisor,
            ValidAudience = jwt.Audiencia,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwt.EstaConfigurada ? jwt.Clave : new string('0', 32))),
            // Sin holgura de reloj: el valor por defecto son 5 minutos, que alargan la vida de un
            // token vencido sin que nadie lo pida.
            ClockSkew = TimeSpan.Zero
        };

        // El hub no puede mandar cabecera Authorization al negociar por WebSocket, asi que
        // SignalR pasa el token por query string. Solo se acepta en esa ruta.
        opciones.Events = new JwtBearerEvents
        {
            OnMessageReceived = contexto =>
            {
                var token = contexto.Request.Query["access_token"];

                if (!string.IsNullOrEmpty(token) &&
                    contexto.HttpContext.Request.Path.StartsWithSegments(CanalBandeja.Ruta))
                {
                    contexto.Token = token;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(opciones =>
{
    opciones.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    // V23: quien administra que, por rol. Se suman a la de respaldo, no la reemplazan.
    Politicas.Registrar(opciones);
});
// Seccion 9.6.3: el canal en vivo hacia la bandeja, y el bucle que le da de comer desde la outbox.
builder.Services.AddSignalR();
builder.Services.AddSingleton<IAvisoBandeja, AvisoBandeja>();
builder.Services.AddHostedService<DifusorNotificaciones>();
// Seccion 9.6.2. Un AddHealthChecks() pelado devuelve Healthy siempre, incluso con la base caida
// y el Worker muerto: seria peor que no tenerlo, porque induce a confiar. Estos dos chequeos son
// los que hacen que /health signifique algo.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<RrhhDbContext>("base", tags: ["listo"])
    .AddCheck<ChequeoWorker>("worker", tags: ["listo"]);

builder.Services.Configure<OpcionesJobForms>(builder.Configuration.GetSection(OpcionesJobForms.Seccion));

// V25: el correo del primer analista de Sistemas, para poner en marcha una base nueva.
builder.Services.Configure<OpcionesArranque>(builder.Configuration.GetSection(OpcionesArranque.Seccion));

var limitePorMinuto = builder.Configuration
    .GetSection(OpcionesJobForms.Seccion)
    .GetValue("LimitePorMinuto", 30);

// Los endpoints del JobForms son los unicos alcanzables desde internet sin autenticacion. Sin un
// tope quedan expuestos a abuso automatizado (Seccion 9.6.1). El webhook de WhatsApp no lleva
// limite a proposito: quien lo llama es 360dialog, y descartarle entregas provoca reintentos.
builder.Services.AddRateLimiter(opciones =>
{
    opciones.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    opciones.AddPolicy(PoliticasLimite.Publico, contexto =>
        RateLimitPartition.GetFixedWindowLimiter(
            contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limitePorMinuto,
                Window = TimeSpan.FromMinutes(1)
            }));
});

builder.Services.AgregarInfraestructura(builder.Configuration);
builder.Services.AgregarReporting(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// En el despliegue on-premise TLS termina en IIS y el trafico llega a Kestrel por HTTP. Sin leer
// las cabeceras reenviadas, la aplicacion veria "http" y una redireccion a HTTPS le devolveria un
// 307 al webhook, que 360dialog cuenta como entrega fallida. Por eso se leen las cabeceras y no se
// redirige aca: de la redireccion se encarga el servidor de borde.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<HubBandeja>(CanalBandeja.Ruta);

// Dos endpoints con proposito distinto. /health/vivo dice solo que el proceso responde, y es el
// que mira IIS para decidir si reciclar el sitio: si mirara la base, una caida de SQL Server
// reiniciaria la Api en bucle sin arreglar nada. /health mira todo y es el que debe vigilar el
// monitoreo, porque es el unico que se pone en rojo cuando el Worker deja de procesar.
app.MapHealthChecks("/health/vivo", new HealthCheckOptions { Predicate = _ => false })
   .AllowAnonymous();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = EscribirSalud
}).AllowAnonymous();

// El detalle importa: un 503 sin decir cual bucle se detuvo obliga a entrar al servidor a mirar
// logs, que es justo lo que el endpoint deberia evitar.
static Task EscribirSalud(HttpContext contexto, HealthReport reporte)
{
    contexto.Response.ContentType = "application/json";

    return contexto.Response.WriteAsJsonAsync(new
    {
        estado = reporte.Status.ToString(),
        duracionMs = reporte.TotalDuration.TotalMilliseconds,
        chequeos = reporte.Entries.ToDictionary(
            e => e.Key,
            e => (object)new
            {
                estado = e.Value.Status.ToString(),
                descripcion = e.Value.Description,
                datos = e.Value.Data.Count == 0 ? null : e.Value.Data
            })
    });
}

app.Run();

/// <summary>Expuesto para que las pruebas de integracion puedan levantar la Api con WebApplicationFactory.</summary>
public partial class Program;
