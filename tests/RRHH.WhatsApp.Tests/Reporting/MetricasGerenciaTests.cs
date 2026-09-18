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

    /// <summary>Lunes 14 de setiembre de 2026, para las pruebas que dependen del día de la semana.</summary>
    private static DateTime Lima(int dia, int hora, int minuto) =>
        new DateTime(2026, 9, dia, hora, minuto, 0, DateTimeKind.Utc).AddHours(5);

    private Task<Contracts.Metricas.MetricasGerencia> MetricasDeLaSemanaAsync() =>
        _entorno.Metricas.ObtenerMetricasAsync(Lima(14, 0, 0), Lima(22, 0, 0));

    /// <summary>
    /// FUN-17 (M7): la unidad de medida es la tanda —lo que el postulante esperó cada vez—, no la
    /// conversación. Un hilo que va y viene tres veces son tres esperas, y antes se medía una sola.
    /// </summary>
    [Fact]
    public async Task Dos_tandas_en_un_hilo_se_miden_por_separado()
    {
        _entorno.ConHorarioComercial();

        _entorno.Entrante(1, Lima(15, 10, 0));
        _entorno.RespuestaDeAnalista(1, Lima(15, 10, 30));
        _entorno.Entrante(1, Lima(15, 14, 0));
        _entorno.RespuestaDeAnalista(1, Lima(15, 14, 20));

        var respuesta = (await MetricasDeLaSemanaAsync()).Respuesta;

        Assert.Equal(2, respuesta.Tandas);
        Assert.Equal(2, respuesta.TandasRespondidas);
        Assert.Equal(25, respuesta.MinutosHabilesPromedio);
    }

    /// <summary>
    /// A15: el viernes a las 17:50 y la respuesta del lunes a las 09:10 son 20 minutos de atención, no
    /// tres días. Medirlo a reloj corrido daba un panel que decía que nadie contesta.
    /// </summary>
    [Fact]
    public async Task Del_viernes_a_la_tarde_al_lunes_son_veinte_minutos_habiles()
    {
        _entorno.ConHorarioComercial();

        // Viernes 18 y lunes 21 de setiembre de 2026.
        _entorno.Entrante(1, Lima(18, 17, 50));
        _entorno.RespuestaDeAnalista(1, Lima(21, 9, 10));

        var respuesta = (await MetricasDeLaSemanaAsync()).Respuesta;

        Assert.Equal(1, respuesta.TandasRespondidas);
        Assert.Equal(20, respuesta.MinutosHabilesPromedio);

        // El de reloj se conserva: es el que muestra cuánto esperó la persona en la vida real.
        Assert.True(respuesta.MinutosPromedio > 3000);
    }

    /// <summary>Tres mensajes seguidos del postulante son una sola espera, que empieza en el primero.</summary>
    [Fact]
    public async Task Los_mensajes_seguidos_del_postulante_son_una_sola_tanda()
    {
        _entorno.ConHorarioComercial();

        _entorno.Entrante(1, Lima(15, 10, 0));
        _entorno.Entrante(1, Lima(15, 10, 5));
        _entorno.Entrante(1, Lima(15, 10, 8));
        _entorno.RespuestaDeAnalista(1, Lima(15, 10, 30));

        var respuesta = (await MetricasDeLaSemanaAsync()).Respuesta;

        Assert.Equal(1, respuesta.Tandas);
        Assert.Equal(30, respuesta.MinutosHabilesPromedio);
    }

    /// <summary>Una espera que nadie cerró se cuenta como tanda: es justo la que hay que mirar.</summary>
    [Fact]
    public async Task Una_tanda_sin_responder_se_cuenta_aparte()
    {
        _entorno.ConHorarioComercial();

        _entorno.Entrante(1, Lima(15, 10, 0));
        _entorno.RespuestaDeAnalista(1, Lima(15, 10, 30));
        _entorno.Entrante(1, Lima(15, 16, 0));

        var respuesta = (await MetricasDeLaSemanaAsync()).Respuesta;

        Assert.Equal(2, respuesta.Tandas);
        Assert.Equal(1, respuesta.TandasRespondidas);
    }

    /// <summary>El plazo es el mismo de la Regla 2, y se mide con la misma vara: horas hábiles (A15).</summary>
    [Fact]
    public async Task El_porcentaje_dentro_del_plazo_se_mide_en_horas_habiles()
    {
        _entorno.ConHorarioComercial();

        // Dentro del plazo: una hora hábil, aunque de por medio pase la noche.
        _entorno.Entrante(1, Lima(15, 17, 30));
        _entorno.RespuestaDeAnalista(1, Lima(16, 9, 30));

        // Fuera: tres horas hábiles del mismo día.
        _entorno.Entrante(2, Lima(16, 10, 0));
        _entorno.RespuestaDeAnalista(2, Lima(16, 13, 0));

        var respuesta = (await MetricasDeLaSemanaAsync()).Respuesta;

        Assert.Equal(2, respuesta.TandasRespondidas);
        Assert.Equal(0.5, respuesta.PorcentajeDentroDelPlazo);
    }

    /// <summary>La actividad de cada analista se mide con la misma vara que el panel.</summary>
    [Fact]
    public async Task El_tiempo_de_cada_analista_sale_de_sus_tandas()
    {
        _entorno.ConHorarioComercial();

        _entorno.Entrante(1, Lima(15, 10, 0));
        _entorno.RespuestaDeAnalista(1, Lima(15, 10, 20), EntornoDeMetricas.TitularId);
        _entorno.Entrante(2, Lima(15, 11, 0));
        _entorno.RespuestaDeAnalista(2, Lima(15, 12, 0), EntornoDeMetricas.RespaldoId);

        var metricas = await MetricasDeLaSemanaAsync();

        var titular = Assert.Single(metricas.Analistas, a => a.AnalistaId == EntornoDeMetricas.TitularId);
        var respaldo = Assert.Single(metricas.Analistas, a => a.AnalistaId == EntornoDeMetricas.RespaldoId);

        Assert.Equal(20, titular.MinutosHabilesPromedioRespuesta);
        Assert.Equal(60, respaldo.MinutosHabilesPromedioRespuesta);
    }

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
