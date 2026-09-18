using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Contracts.Metricas;
using RRHH.WhatsApp.Domain.Calendario;
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
        var tandas = await TandasAsync(mensajes, ct);

        return new MetricasGerencia(
            desdeUtc,
            hastaUtc,
            await ArmarRespuestaAsync(primeras, tandas, desdeUtc, hastaUtc, ct),
            ArmarConversion(postulaciones),
            await ArmarAnalistasAsync(mensajes, postulaciones, primeras, tandas, ct),
            await ArmarCuentasAsync(postulaciones, ct));
    }

    /// <summary>
    /// FUN-17 (M7): las esperas del periodo. Una tanda empieza con el primer entrante posterior a la
    /// ultima respuesta humana y termina con esa respuesta; los mensajes seguidos del postulante son una
    /// sola espera, y el bot no la cierra, porque contesta en segundos y no es el servicio que se mide.
    /// <para>
    /// Se mide en minutos habiles con el mismo <see cref="CalendarioLaboral"/> que usa el escalamiento
    /// (A15): a reloj corrido, un mensaje del viernes a la tarde respondido el lunes da tres dias.
    /// </para>
    /// </summary>
    private async Task<List<Tanda>> TandasAsync(IReadOnlyList<LineaMensaje> mensajes, CancellationToken ct)
    {
        if (mensajes.Count == 0)
            return [];

        var ids = mensajes.Select(m => m.ConversacionId).Distinct().ToList();

        var cuentaDelHilo = await db.Conversaciones
            .Where(c => ids.Contains(c.ConversacionId))
            .Select(c => new { c.ConversacionId, c.CuentaContextoId })
            .ToDictionaryAsync(c => c.ConversacionId, c => c.CuentaContextoId, ct);

        var horarios = await db.HorariosAtencion.ToListAsync(ct);

        var general = horarios.Where(h => h.CuentaId is null).ToList();
        var porCuenta = horarios.Where(h => h.CuentaId is not null).ToLookup(h => h.CuentaId!.Value);

        var tandas = new List<Tanda>();

        foreach (var grupo in mensajes.GroupBy(m => m.ConversacionId))
        {
            // El horario de la cuenta en contexto, o el general: es el mismo criterio con el que la
            // Regla 3 decide si el hilo esta dentro de horario.
            var propios = cuentaDelHilo.GetValueOrDefault(grupo.Key) is { } cuentaId
                ? porCuenta[cuentaId].ToList()
                : [];

            var tramos = propios.Count > 0 ? propios : general;

            DateTime? inicio = null;

            foreach (var mensaje in grupo.OrderBy(m => m.FechaEnvio))
            {
                if (mensaje.Direccion == DireccionMensaje.Entrante)
                {
                    inicio ??= mensaje.FechaEnvio;
                    continue;
                }

                if (mensaje.AnalistaId is null || inicio is not { } espera)
                    continue;

                tandas.Add(new Tanda(
                    mensaje.AnalistaId,
                    CalendarioLaboral.MinutosHabilesEntre(tramos, espera, mensaje.FechaEnvio)));

                inicio = null;
            }

            // Una espera que nadie cerro: es justo la que hay que mirar, asi que se cuenta igual.
            if (inicio is not null)
                tandas.Add(new Tanda(null, null));
        }

        return tandas;
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
        Dictionary<int, PrimeraRespuesta> primeras, IReadOnlyList<Tanda> tandas,
        DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct)
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

        var habiles = tandas
            .Where(t => t.MinutosHabiles is not null)
            .Select(t => t.MinutosHabiles!.Value)
            .OrderBy(m => m)
            .ToList();

        var enPlazo = habiles.Count(m => m <= horasPlazo * 60);

        return new MetricasRespuesta(
            ConversacionesConRespuesta: minutos.Count,
            ConversacionesSinResponder: primeras.Count - minutos.Count,
            MinutosPromedio: minutos.Count == 0 ? null : Math.Round(minutos.Average(), 1),
            MinutosMediana: Mediana(minutos),
            DentroDelPlazo: minutos.Count(m => m <= horasPlazo * 60),
            Escalamientos: escalamientos,
            Tandas: tandas.Count,
            TandasRespondidas: habiles.Count,
            MinutosHabilesPromedio: habiles.Count == 0 ? null : Math.Round(habiles.Average(), 1),
            MinutosHabilesMediana: Mediana(habiles),
            PorcentajeDentroDelPlazo: Tasa(enPlazo, habiles.Count));
    }

    private static MetricasConversion ArmarConversion(IReadOnlyList<LineaPostulacion> postulaciones)
    {
        var total = postulaciones.Count;
        var contratados = postulaciones.Count(p => p.Estado == EstadoPostulacion.Contratado);

        return new MetricasConversion(
            Postulaciones: total,
            Contratados: contratados,
            Descartados: postulaciones.Count(p => p.Estado == EstadoPostulacion.Descartado),
            // A2: un reingreso es un proceso vivo. Contarlo aparte haria que el panel mostrara menos
            // gente en proceso de la que los analistas estan atendiendo.
            EnProceso: postulaciones.Count(p => p.Estado is EstadoPostulacion.EnProceso or EstadoPostulacion.Reingreso),
            TasaConversion: Tasa(contratados, total));
    }

    private async Task<IReadOnlyList<ActividadAnalista>> ArmarAnalistasAsync(
        IReadOnlyList<LineaMensaje> mensajes,
        IReadOnlyList<LineaPostulacion> postulaciones,
        Dictionary<int, PrimeraRespuesta> primeras,
        IReadOnlyList<Tanda> tandas,
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

            // FUN-17: sus esperas, medidas con la misma vara que el panel.
            var suyas = tandas
                .Where(t => t.AnalistaId == a.AnalistaId && t.MinutosHabiles is not null)
                .Select(t => t.MinutosHabiles!.Value)
                .ToList();

            return new ActividadAnalista(
                a.AnalistaId,
                a.Nombre,
                ConversacionesAtendidas: suyos.Select(m => m.ConversacionId).Distinct().Count(),
                MensajesEnviados: suyos.Count,
                MinutosPromedioPrimeraRespuesta: tiempos.Count == 0 ? null : Math.Round(tiempos.Average(), 1),
                Contratados: deSuCartera.Count(p => p.Estado == EstadoPostulacion.Contratado),
                Descartados: deSuCartera.Count(p => p.Estado == EstadoPostulacion.Descartado),
                MinutosHabilesPromedioRespuesta: suyas.Count == 0 ? null : Math.Round(suyas.Average(), 1));
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

    /// <summary>Una espera del postulante y, si alguien la cerro, quien fue y cuanto tardo en horas habiles.</summary>
    private sealed record Tanda(int? AnalistaId, double? MinutosHabiles);
}
