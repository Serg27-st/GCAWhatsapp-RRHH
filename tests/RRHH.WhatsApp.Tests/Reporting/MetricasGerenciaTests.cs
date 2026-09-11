using RRHH.WhatsApp.Domain.Enums;

namespace RRHH.WhatsApp.Tests.Reporting;

/// <summary>
/// Regla 18 — panel de gerencia. Las tres metricas que pide el dossier: tiempo de primera
/// respuesta, tasa de conversion y actividad por analista.
/// </summary>
public class MetricasGerenciaTests : IDisposable
{
    private readonly EntornoDeMetricas _entorno = new();

    private static readonly DateTime Desde = EntornoDeMetricas.Ahora.AddDays(-7);
    private static readonly DateTime Hasta = EntornoDeMetricas.Ahora.AddDays(1);

    private Task<Contracts.Metricas.MetricasGerencia> MetricasAsync() =>
        _entorno.Metricas.ObtenerMetricasAsync(Desde, Hasta);

    [Fact]
    public async Task El_promedio_y_la_mediana_de_primera_respuesta_salen_de_los_mensajes()
    {
        var ayer = EntornoDeMetricas.Ahora.AddDays(-1);

        _entorno.ConversacionConRespuesta(1, ayer, minutosHastaRespuesta: 10);
        _entorno.ConversacionConRespuesta(2, ayer, minutosHastaRespuesta: 20);
        _entorno.ConversacionConRespuesta(3, ayer, minutosHastaRespuesta: 300);

        var metricas = await MetricasAsync();

        Assert.Equal(3, metricas.Respuesta.ConversacionesConRespuesta);
        Assert.Equal(110, metricas.Respuesta.MinutosPromedio);

        // La mediana es el motivo de que este el par: un caso de 5 horas triplica el promedio.
        Assert.Equal(20, metricas.Respuesta.MinutosMediana);
    }

    [Fact]
    public async Task Un_hilo_sin_respuesta_humana_se_cuenta_aparte_y_no_ensucia_el_promedio()
    {
        var ayer = EntornoDeMetricas.Ahora.AddDays(-1);

        _entorno.ConversacionConRespuesta(1, ayer, minutosHastaRespuesta: 30);
        _entorno.ConversacionConRespuesta(2, ayer, minutosHastaRespuesta: null);

        var metricas = await MetricasAsync();

        Assert.Equal(1, metricas.Respuesta.ConversacionesConRespuesta);
        Assert.Equal(1, metricas.Respuesta.ConversacionesSinResponder);
        Assert.Equal(30, metricas.Respuesta.MinutosPromedio);
    }

    [Fact]
    public async Task El_mensaje_del_bot_no_cuenta_como_primera_respuesta()
    {
        // El menu sale en segundos: contarlo daria un panel que dice que se responde al instante.
        var ayer = EntornoDeMetricas.Ahora.AddDays(-1);

        _entorno.ConversacionConRespuesta(1, ayer, minutosHastaRespuesta: null);
        _entorno.MensajeDelBot(1, ayer.AddSeconds(2));

        var metricas = await MetricasAsync();

        Assert.Equal(0, metricas.Respuesta.ConversacionesConRespuesta);
        Assert.Equal(1, metricas.Respuesta.ConversacionesSinResponder);
        Assert.Null(metricas.Respuesta.MinutosPromedio);
    }

    [Fact]
    public async Task El_plazo_se_mide_contra_el_mismo_parametro_que_usa_la_Regla_2()
    {
        var ayer = EntornoDeMetricas.Ahora.AddDays(-1);

        _entorno.ConversacionConRespuesta(1, ayer, minutosHastaRespuesta: 60);
        _entorno.ConversacionConRespuesta(2, ayer, minutosHastaRespuesta: 119);
        _entorno.ConversacionConRespuesta(3, ayer, minutosHastaRespuesta: 200);

        var metricas = await MetricasAsync();

        Assert.Equal(2, metricas.Respuesta.DentroDelPlazo);
    }

    [Fact]
    public async Task Los_escalamientos_del_periodo_se_cuentan_desde_la_auditoria()
    {
        _entorno.Escalamiento(EntornoDeMetricas.Ahora.AddDays(-1));
        _entorno.Escalamiento(EntornoDeMetricas.Ahora.AddDays(-2));
        _entorno.Escalamiento(EntornoDeMetricas.Ahora.AddDays(-90));

        var metricas = await MetricasAsync();

        Assert.Equal(2, metricas.Respuesta.Escalamientos);
    }

    [Fact]
    public async Task La_tasa_de_conversion_es_contratados_sobre_postulaciones()
    {
        var ayer = EntornoDeMetricas.Ahora.AddDays(-1);

        _entorno.Postulacion(EstadoPostulacion.Contratado, ayer);
        _entorno.Postulacion(EstadoPostulacion.Descartado, ayer);
        _entorno.Postulacion(EstadoPostulacion.Descartado, ayer);
        _entorno.Postulacion(EstadoPostulacion.EnProceso, ayer);

        var metricas = await MetricasAsync();

        Assert.Equal(4, metricas.Conversion.Postulaciones);
        Assert.Equal(1, metricas.Conversion.Contratados);
        Assert.Equal(2, metricas.Conversion.Descartados);
        Assert.Equal(1, metricas.Conversion.EnProceso);
        Assert.Equal(0.25, metricas.Conversion.TasaConversion);
    }

    [Fact]
    public async Task Sin_postulaciones_la_tasa_es_cero_y_no_una_division_por_cero()
    {
        var metricas = await MetricasAsync();

        Assert.Equal(0, metricas.Conversion.Postulaciones);
        Assert.Equal(0, metricas.Conversion.TasaConversion);
    }

    [Fact]
    public async Task La_actividad_se_reparte_por_analista()
    {
        var ayer = EntornoDeMetricas.Ahora.AddDays(-1);

        _entorno.ConversacionConRespuesta(1, ayer, 10, EntornoDeMetricas.TitularId);
        _entorno.ConversacionConRespuesta(2, ayer, 30, EntornoDeMetricas.TitularId);
        _entorno.ConversacionConRespuesta(3, ayer, 50, EntornoDeMetricas.RespaldoId);

        _entorno.Postulacion(EstadoPostulacion.Contratado, ayer, EntornoDeMetricas.TitularId);
        _entorno.Postulacion(EstadoPostulacion.Descartado, ayer, EntornoDeMetricas.RespaldoId);

        var metricas = await MetricasAsync();

        var titular = metricas.Analistas.Single(a => a.AnalistaId == EntornoDeMetricas.TitularId);
        var respaldo = metricas.Analistas.Single(a => a.AnalistaId == EntornoDeMetricas.RespaldoId);

        Assert.Equal(2, titular.ConversacionesAtendidas);
        Assert.Equal(2, titular.MensajesEnviados);
        Assert.Equal(20, titular.MinutosPromedioPrimeraRespuesta);
        Assert.Equal(1, titular.Contratados);

        Assert.Equal(1, respaldo.ConversacionesAtendidas);
        Assert.Equal(50, respaldo.MinutosPromedioPrimeraRespuesta);
        Assert.Equal(1, respaldo.Descartados);
    }

    [Fact]
    public async Task Lo_que_cae_fuera_del_periodo_no_entra()
    {
        _entorno.ConversacionConRespuesta(1, EntornoDeMetricas.Ahora.AddDays(-1), 15);
        _entorno.ConversacionConRespuesta(2, EntornoDeMetricas.Ahora.AddDays(-60), 999);
        _entorno.Postulacion(EstadoPostulacion.Contratado, EntornoDeMetricas.Ahora.AddDays(-60));

        var metricas = await MetricasAsync();

        Assert.Equal(1, metricas.Respuesta.ConversacionesConRespuesta);
        Assert.Equal(15, metricas.Respuesta.MinutosPromedio);
        Assert.Equal(0, metricas.Conversion.Postulaciones);
    }

    [Fact]
    public async Task Una_cuenta_sin_postulaciones_en_el_periodo_no_aparece()
    {
        var metricas = await MetricasAsync();

        Assert.Empty(metricas.Cuentas);
    }

    [Fact]
    public async Task Un_periodo_invertido_se_rechaza()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _entorno.Metricas.ObtenerMetricasAsync(Hasta, Desde));
    }

    [Fact]
    public void El_modelo_de_reporting_no_deja_escribir()
    {
        // La garantia es por construccion: un reporte no puede tocar las tablas de nadie.
        Assert.Throws<InvalidOperationException>(() => _entorno.Db.SaveChanges());
    }

    public void Dispose() => _entorno.Dispose();
}
