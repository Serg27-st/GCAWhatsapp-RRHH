using Microsoft.Extensions.Diagnostics.HealthChecks;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Api.Salud;

/// <summary>
/// Comprueba que los bucles del Worker sigan latiendo (Sección 9.6.2).
/// <para>
/// El escalamiento de 2h, el recordatorio de 24h, el aviso de 48h y el archivado de 90 días
/// dependen enteramente de ese proceso, y un Worker detenido no avisa: simplemente deja de pasar
/// lo que tenía que pasar. Sin esta comprobación nadie se entera hasta que un postulante reclama.
/// </para>
/// </summary>
public sealed class ChequeoWorker(ILatidoServicio latidos, TimeProvider reloj) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext contexto, CancellationToken ct = default)
    {
        IReadOnlyList<LatidoServicio> registrados;

        try
        {
            registrados = await latidos.ListarAsync(ct);
        }
        catch (Exception ex)
        {
            // Si no se puede leer la tabla, el problema es la base y ya lo reporta su propio
            // chequeo. Aca solo se admite que no se sabe, en vez de dar por bueno el Worker.
            return HealthCheckResult.Unhealthy("No se pudo leer el estado del Worker.", ex);
        }

        // ARQ-01: el instante sale del reloj inyectable, como en el resto del sistema. Con el del
        // sistema aca adentro, el umbral de silencio de cada bucle no se podia probar sin esperarlo.
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var detenidos = new List<string>();
        var datos = new Dictionary<string, object>();

        foreach (var servicio in ServiciosVigilados.Todos)
        {
            var latido = registrados.FirstOrDefault(l => l.Servicio == servicio);

            if (latido is null)
            {
                // Nunca latió: o el Worker no arrancó nunca, o esta base es nueva.
                detenidos.Add($"{servicio}: sin latido registrado");
                datos[servicio] = "sin latido";
                continue;
            }

            var silencio = ahora - latido.FechaUtc;
            var tolerancia = TimeSpan.FromSeconds(latido.ToleranciaSegundos);

            datos[servicio] = new
            {
                ultimoLatidoUtc = latido.FechaUtc,
                hace = $"{silencio.TotalMinutes:F1} min",
                latido.Detalle
            };

            if (silencio > tolerancia)
                detenidos.Add($"{servicio}: {silencio.TotalMinutes:F0} min sin latir");
        }

        return detenidos.Count == 0
            ? HealthCheckResult.Healthy("El Worker está procesando.", datos)
            : HealthCheckResult.Unhealthy(
                "Hay bucles del Worker detenidos: " + string.Join("; ", detenidos), data: datos);
    }
}
