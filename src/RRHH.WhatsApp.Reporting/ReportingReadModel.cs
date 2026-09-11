using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Contracts.Metricas;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Reporting.Persistencia;

namespace RRHH.WhatsApp.Reporting;

/// <summary>
/// Calcula el panel de la Regla 18 sobre los datos que el flujo ya deja registrados.
/// <para>
/// El grueso del calculo se hace en memoria sobre proyecciones minimas y no con agregados en SQL.
/// A 16.000 interacciones al mes un periodo tipico son unos pocos miles de filas de dos columnas,
/// y la version legible gana: la primera respuesta depende del orden entre mensajes entrantes y
/// salientes, que en SQL sale como una consulta que despues nadie se anima a tocar.
/// </para>
/// </summary>
public sealed class ReportingReadModel(ReportingDbContext db) : IReportingReadModel
{
    public async Task<MetricasGerencia> ObtenerMetricasAsync(
        DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct = default)
    {
        if (hastaUtc <= desdeUtc)
            throw new ArgumentException("El periodo termina antes de empezar.", nameof(hastaUtc));

        var mensajes = await db.Mensajes
            .Where(m => m.FechaEnvio >= desdeUtc && m.FechaEnvio < hastaUtc)
            .Select(m => new LineaMensaje(m.ConversacionId, m.Direccion, m.AnalistaId, m.FechaEnvio))
            .ToListAsync(ct);

        var postulaciones = await db.Postulaciones
            .Where(p => p.FechaCreacion >= desdeUtc && p.FechaCreacion < hastaUtc)
            .Select(p => new LineaPostulacion(p.CuentaId, p.AnalistaAsignadoId, p.Estado))
            .ToListAsync(ct);

        var primeras = PrimerasRespuestas(mensajes);

        return new MetricasGerencia(
            desdeUtc,
            hastaUtc,
            await ArmarRespuestaAsync(primeras, desdeUtc, hastaUtc, ct),
            ArmarConversion(postulaciones),
            await ArmarAnalistasAsync(mensajes, postulaciones, primeras, ct),
            await ArmarCuentasAsync(postulaciones, ct));
    }

    /// <summary>
    /// Primera respuesta humana por conversacion. Se exige que sea de un analista y posterior al
    /// primer mensaje del postulante: el menu del bot sale en segundos y contarlo convertiria la
    /// metrica en un numero sin relacion con el servicio.
    /// </summary>
    private static Dictionary<int, PrimeraRespuesta> PrimerasRespuestas(IReadOnlyList<LineaMensaje> mensajes)
    {
        var resultado = new Dictionary<int, PrimeraRespuesta>();

        foreach (var grupo in mensajes.GroupBy(m => m.ConversacionId))
        {
            var ordenados = grupo.OrderBy(m => m.FechaEnvio).ToList();

            var entrante = ordenados.FirstOrDefault(m => m.Direccion == DireccionMensaje.Entrante);

            if (entrante is null)
                continue;

            var respuesta = ordenados.FirstOrDefault(
                m => m.Direccion == DireccionMensaje.Saliente
                  && m.AnalistaId is not null
                  && m.FechaEnvio >= entrante.FechaEnvio);

            resultado[grupo.Key] = new PrimeraRespuesta(
                respuesta?.AnalistaId,
                respuesta is null ? null : (respuesta.FechaEnvio - entrante.FechaEnvio).TotalMinutes);
        }

        return resultado;
    }


    private async Task<MetricasRespuesta> ArmarRespuestaAsync(
        Dictionary<int, PrimeraRespuesta> primeras, DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct)
    {
        var minutos = primeras.Values
            .Where(p => p.Minutos is not null)
            .Select(p => p.Minutos!.Value)
            .OrderBy(m => m)
            .ToList();

        // El mismo plazo con el que la Regla 2 escala: medir contra otro numero daria un panel que
        // no coincide con lo que el sistema hace.
        var horasPlazo = await ParametroAsync(ClavesConfiguracion.EscalamientoHoras, 2, ct);

        var escalamientos = await db.Auditorias
            .CountAsync(a => a.Accion == "Escalamiento"
                          && a.Fecha >= desdeUtc && a.Fecha < hastaUtc, ct);

        return new MetricasRespuesta(
            ConversacionesConRespuesta: minutos.Count,
            ConversacionesSinResponder: primeras.Count - minutos.Count,
            MinutosPromedio: minutos.Count == 0 ? null : Math.Round(minutos.Average(), 1),
            MinutosMediana: Mediana(minutos),
            DentroDelPlazo: minutos.Count(m => m <= horasPlazo * 60),
            Escalamientos: escalamientos);
    }

    private static MetricasConversion ArmarConversion(IReadOnlyList<LineaPostulacion> postulaciones)
    {
        var total = postulaciones.Count;
        var contratados = postulaciones.Count(p => p.Estado == EstadoPostulacion.Contratado);

        return new MetricasConversion(
            Postulaciones: total,
            Contratados: contratados,
            Descartados: postulaciones.Count(p => p.Estado == EstadoPostulacion.Descartado),
            EnProceso: postulaciones.Count(p => p.Estado == EstadoPostulacion.EnProceso),
            TasaConversion: Tasa(contratados, total));
    }

    private async Task<IReadOnlyList<ActividadAnalista>> ArmarAnalistasAsync(
        IReadOnlyList<LineaMensaje> mensajes,
        IReadOnlyList<LineaPostulacion> postulaciones,
        Dictionary<int, PrimeraRespuesta> primeras,
        CancellationToken ct)
    {
        var nombres = await db.Analistas
            .Where(a => a.Activo)
            .Select(a => new { a.AnalistaId, a.Nombre })
            .ToListAsync(ct);

        var salientes = mensajes
            .Where(m => m.Direccion == DireccionMensaje.Saliente && m.AnalistaId is not null)
            .ToList();

        var filas = nombres.Select(a =>
        {
            var suyos = salientes.Where(m => m.AnalistaId == a.AnalistaId).ToList();

            var tiempos = primeras.Values
                .Where(p => p.AnalistaId == a.AnalistaId && p.Minutos is not null)
                .Select(p => p.Minutos!.Value)
                .ToList();

            var deSuCartera = postulaciones.Where(p => p.AnalistaAsignadoId == a.AnalistaId).ToList();

            return new ActividadAnalista(
                a.AnalistaId,
                a.Nombre,
                ConversacionesAtendidas: suyos.Select(m => m.ConversacionId).Distinct().Count(),
                MensajesEnviados: suyos.Count,
                MinutosPromedioPrimeraRespuesta: tiempos.Count == 0 ? null : Math.Round(tiempos.Average(), 1),
                Contratados: deSuCartera.Count(p => p.Estado == EstadoPostulacion.Contratado),
                Descartados: deSuCartera.Count(p => p.Estado == EstadoPostulacion.Descartado));
        });

        return [.. filas.OrderByDescending(a => a.MensajesEnviados).ThenBy(a => a.Nombre)];
    }

    private async Task<IReadOnlyList<MetricasCuenta>> ArmarCuentasAsync(
        IReadOnlyList<LineaPostulacion> postulaciones, CancellationToken ct)
    {
        var nombres = await db.Cuentas
            .Where(c => c.Activo)
            .Select(c => new { c.CuentaId, c.Nombre })
            .ToListAsync(ct);

        var filas = nombres.Select(c =>
        {
            var suyas = postulaciones.Where(p => p.CuentaId == c.CuentaId).ToList();
            var contratados = suyas.Count(p => p.Estado == EstadoPostulacion.Contratado);

            return new MetricasCuenta(
                c.CuentaId, c.Nombre, suyas.Count, contratados, Tasa(contratados, suyas.Count));
        });

        // Una cuenta sin postulaciones en el periodo no aporta nada al panel; solo lo alarga.
        return [.. filas.Where(c => c.Postulaciones > 0).OrderByDescending(c => c.Postulaciones)];
    }

    private async Task<int> ParametroAsync(string clave, int porDefecto, CancellationToken ct)
    {
        var valor = await db.ConfiguracionReglas
            .Where(c => c.Clave == clave)
            .Select(c => c.Valor)
            .FirstOrDefaultAsync(ct);

        return int.TryParse(valor, out var n) && n > 0 ? n : porDefecto;
    }

    private static double Tasa(int parte, int total) =>
        total == 0 ? 0 : Math.Round((double)parte / total, 4);

    /// <summary>Espera la lista ya ordenada de menor a mayor.</summary>
    private static double? Mediana(IReadOnlyList<double> ordenados)
    {
        if (ordenados.Count == 0)
            return null;

        var medio = ordenados.Count / 2;

        return Math.Round(
            ordenados.Count % 2 == 1
                ? ordenados[medio]
                : (ordenados[medio - 1] + ordenados[medio]) / 2,
            1);
    }
    private sealed record LineaMensaje(
        int ConversacionId, DireccionMensaje Direccion, int? AnalistaId, DateTime FechaEnvio);

    private sealed record LineaPostulacion(int CuentaId, int? AnalistaAsignadoId, EstadoPostulacion Estado);

    private sealed record PrimeraRespuesta(int? AnalistaId, double? Minutos);
}
