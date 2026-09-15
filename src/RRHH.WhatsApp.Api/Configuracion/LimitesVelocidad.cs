using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Api.Seguridad;

namespace RRHH.WhatsApp.Api.Configuracion;

/// <summary>
/// Registro del limitador de velocidad de los endpoints publicos (Seccion 9.6.1). Separado de
/// <c>Program.cs</c> para poder probar las particiones sin levantar el host: son las unicas rutas
/// alcanzables desde internet sin autenticacion, asi que lo que decide aca es la unica barrera
/// contra abuso automatizado. El webhook de WhatsApp no lleva limite a proposito: quien lo llama es
/// 360dialog, y descartarle entregas provoca reintentos.
/// </summary>
public static class LimitesVelocidad
{
    /// <summary>
    /// Clave fija de la particion de quien trae el secreto correcto: en la practica, el Apps Script.
    /// No hay una por script porque el secreto es unico y compartido (Seccion 9.6.1).
    /// </summary>
    internal const string ParticionAppsScript = "apps-script";

    /// <summary>
    /// Prefijo para la particion por IP dentro de <see cref="PoliticasLimite.WebhookGoogle"/>. Tiene
    /// que ser distinta de la clave que usa <see cref="PoliticasLimite.Publico"/> para la misma IP:
    /// si compartieran clave, alguien sin secreto que agota su cupo en un endpoint le robaria
    /// presupuesto al otro.
    /// </summary>
    internal const string PrefijoIpWebhook = "webhook-ip:";

    public static void Configurar(RateLimiterOptions opciones, OpcionesJobForms jobForms)
    {
        opciones.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        // El Apps Script espera lo que diga Retry-After antes de reintentar un 429 (COR-15). El
        // middleware no la agrega por su cuenta: sin esto el script caeria siempre a su backoff a
        // ciegas, que puede reintentar antes de que se abra la ventana y gastar sus 5 intentos.
        opciones.OnRejected = (contexto, _) =>
        {
            AgregarRetryAfter(contexto);
            return ValueTask.CompletedTask;
        };

        opciones.AddPolicy(PoliticasLimite.Publico, contexto =>
            RateLimitPartition.GetFixedWindowLimiter(
                contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = jobForms.LimitePorMinuto,
                    Window = TimeSpan.FromMinutes(1)
                }));

        opciones.AddPolicy(PoliticasLimite.WebhookGoogle, ParticionWebhookGoogle);
    }

    /// <summary>
    /// Particionado por secreto valido y no por IP (COR-15; desvio consciente de la clave fija
    /// "apps-script" para todo el trafico que proponia el paso previo del backlog): las IPs de
    /// salida de Apps Script son compartidas entre scripts de distintos duenos, asi que particionar
    /// por IP dejaria fuera al script legitimo junto con quien abusa. Sin secreto, o con uno que no
    /// coincide, no hay forma de distinguir al script real de un tercero: cae al mismo tope por IP
    /// que el resto de los endpoints publicos, pero con una clave distinta para que agotar ese cupo
    /// no le robe presupuesto a <see cref="PoliticasLimite.Publico"/>. Esto solo decide el cupo: el
    /// controlador sigue devolviendo 401 cuando el secreto no coincide.
    /// <para>
    /// Se lee <see cref="OpcionesJobForms"/> por <c>IOptions</c> desde <c>RequestServices</c> en vez
    /// de capturarla al registrar el limitador, para usar la misma instancia con la que el
    /// controlador valida el secreto en cada solicitud. Es publica para que las pruebas la ejerzan
    /// sin levantar el host.
    /// </para>
    /// </summary>
    /// <summary>Segundos hasta que se abre la ventana, redondeados hacia arriba para no llegar antes.</summary>
    public static void AgregarRetryAfter(OnRejectedContext contexto)
    {
        if (contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera))
            contexto.HttpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(espera.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public static RateLimitPartition<string> ParticionWebhookGoogle(HttpContext contexto)
    {
        var opciones = contexto.RequestServices
            .GetRequiredService<IOptions<OpcionesJobForms>>().Value;

        if (SecretoJobForms.EsValido(contexto.Request.Headers, opciones))
        {
            return RateLimitPartition.GetFixedWindowLimiter(
                ParticionAppsScript,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = opciones.LimitePorMinutoWebhook,
                    Window = TimeSpan.FromMinutes(1)
                });
        }

        var ip = contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocida";

        return RateLimitPartition.GetFixedWindowLimiter(
            PrefijoIpWebhook + ip,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = opciones.LimitePorMinuto,
                Window = TimeSpan.FromMinutes(1)
            });
    }
}
