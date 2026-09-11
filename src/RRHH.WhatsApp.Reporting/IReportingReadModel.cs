using RRHH.WhatsApp.Contracts.Metricas;

namespace RRHH.WhatsApp.Reporting;

/// <summary>
/// Regla 18: expone tiempos de respuesta, tasa de conversion y actividad por analista sin que
/// nadie tenga que consultar las tablas transaccionales por su cuenta.
/// </summary>
public interface IReportingReadModel
{
    Task<MetricasGerencia> ObtenerMetricasAsync(
        DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct = default);
}
