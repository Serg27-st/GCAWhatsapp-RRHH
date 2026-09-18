using Microsoft.Extensions.Diagnostics.HealthChecks;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Api.Salud;

/// <summary>
/// Pone el health en Degraded mientras haya alertas que dejan postulantes sin respuesta (V32).
/// <para>
/// Una plantilla sin aprobar o una vacante sin formulario no tumban el sistema —la Api y el Worker
/// siguen andando—, por eso no es Unhealthy. Pero son justo lo que un monitoreo tiene que ver: el
/// bot calla y nadie se entera hasta que un postulante reclama. Las demas alertas (cuenta sin
/// respaldo, menu sin opciones) se ven en el panel, sin degradar.
/// </para>
/// </summary>
public sealed class ChequeoAlertas(IAlertaOperativaService alertas) : IHealthCheck
{
    private static readonly HashSet<string> Degradan =
        [TiposAlerta.PlantillaNoAprobada, TiposAlerta.VacanteSinFormulario];

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext contexto, CancellationToken ct = default)
    {
        IReadOnlyList<AlertaOperativa> abiertas;

        try
        {
            abiertas = await alertas.ListarAbiertasAsync(ct);
        }
        catch (Exception ex)
        {
            // Si la tabla no se puede leer, el problema es la base y ya lo reporta su chequeo.
            return HealthCheckResult.Degraded("No se pudieron leer las alertas operativas.", ex);
        }

        var graves = abiertas.Where(a => Degradan.Contains(a.Tipo)).ToList();

        if (graves.Count == 0)
            return HealthCheckResult.Healthy($"{abiertas.Count} alerta(s) abierta(s), ninguna deja postulantes sin respuesta.");

        var datos = graves.ToDictionary(
            a => $"{a.Tipo}:{a.Clave}",
            a => (object)new { a.Ocurrencias, ultimaUtc = a.FechaUltima, a.Detalle });

        return HealthCheckResult.Degraded(
            "Hay postulantes sin respuesta por falta de configuracion: "
            + string.Join("; ", graves.Select(a => $"{a.Tipo} {a.Clave} ({a.Ocurrencias})")),
            data: datos);
    }
}
