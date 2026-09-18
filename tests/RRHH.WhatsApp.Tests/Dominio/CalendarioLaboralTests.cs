using RRHH.WhatsApp.Domain.Calendario;
using RRHH.WhatsApp.Domain.Entidades;

namespace RRHH.WhatsApp.Tests.Dominio;

/// <summary>
/// T2.01 (V31, ARQ-08): el cálculo de horas hábiles es código puro en Domain, para que lo compartan
/// el aviso fuera de horario (R3), el escalamiento (R2) y el panel de métricas (R18) sin que Reporting
/// dependa de Infrastructure. Las horas se escriben en hora de Lima y se convierten a UTC como las
/// guarda la base.
/// </summary>
public class CalendarioLaboralTests
{
    /// <summary>Lunes 14 de setiembre de 2026 en Lima. El viernes de esa semana es el 18.</summary>
    private static DateTime Lima(int dia, int hora, int minuto = 0) =>
        ZonaHorariaPeru.AUtc(new DateTime(2026, 9, dia, hora, minuto, 0));

    private static HorarioAtencion Tramo(DayOfWeek dia, int desde, int hasta, int desdeMinuto = 0, int hastaMinuto = 0) =>
        new() { DiaSemana = dia, HoraInicio = new TimeOnly(desde, desdeMinuto), HoraFin = new TimeOnly(hasta, hastaMinuto) };

    private static List<HorarioAtencion> LunesAViernes(int desde = 9, int hasta = 18) =>
        [.. new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
            .Select(d => Tramo(d, desde, hasta))];

    [Fact]
    public void El_viernes_a_las_20_el_periodo_fuera_de_horario_empezo_a_las_18_y_se_abre_el_lunes_a_las_9()
    {
        var tramos = LunesAViernes();
        var viernesNoche = Lima(18, 20);

        Assert.False(CalendarioLaboral.EstaEnHorario(tramos, viernesNoche));
        Assert.Equal(Lima(18, 18), CalendarioLaboral.InicioPeriodoFueraDeHorario(tramos, viernesNoche));
        Assert.Equal(Lima(21, 9), CalendarioLaboral.ProximaApertura(tramos, viernesNoche));
    }

    [Fact]
    public void Dentro_del_horario_no_hay_periodo_fuera_de_horario()
    {
        var tramos = LunesAViernes();
        var miercolesTarde = Lima(16, 15, 30);

        Assert.True(CalendarioLaboral.EstaEnHorario(tramos, miercolesTarde));
        Assert.Null(CalendarioLaboral.InicioPeriodoFueraDeHorario(tramos, miercolesTarde));
    }

    [Fact]
    public void En_una_jornada_partida_el_almuerzo_es_su_propio_periodo_fuera_de_horario()
    {
        List<HorarioAtencion> tramos = [Tramo(DayOfWeek.Monday, 9, 13), Tramo(DayOfWeek.Monday, 14, 18)];
        var almuerzo = Lima(14, 13, 30);

        Assert.False(CalendarioLaboral.EstaEnHorario(tramos, almuerzo));
        Assert.Equal(Lima(14, 13), CalendarioLaboral.InicioPeriodoFueraDeHorario(tramos, almuerzo));
        Assert.Equal(Lima(14, 14), CalendarioLaboral.ProximaApertura(tramos, almuerzo));
    }

    [Fact]
    public void Un_periodo_fuera_de_horario_que_cruza_la_medianoche_sigue_siendo_el_mismo()
    {
        // Lunes 22:00 y martes 02:00 están en el mismo periodo: un solo aviso por periodo (A8).
        var tramos = LunesAViernes();

        Assert.Equal(Lima(14, 18), CalendarioLaboral.InicioPeriodoFueraDeHorario(tramos, Lima(14, 22)));
        Assert.Equal(Lima(14, 18), CalendarioLaboral.InicioPeriodoFueraDeHorario(tramos, Lima(15, 2)));
        Assert.Equal(Lima(15, 9), CalendarioLaboral.ProximaApertura(tramos, Lima(15, 2)));
    }

    [Fact]
    public void Sin_tramos_no_hay_periodo_ni_apertura_y_se_asume_dentro_de_horario()
    {
        // Sin horario cargado no se puede afirmar que algo está fuera de jornada (R3).
        List<HorarioAtencion> tramos = [];

        Assert.True(CalendarioLaboral.EstaEnHorario(tramos, Lima(18, 20)));
        Assert.Null(CalendarioLaboral.InicioPeriodoFueraDeHorario(tramos, Lima(18, 20)));
        Assert.Null(CalendarioLaboral.ProximaApertura(tramos, Lima(18, 20)));
    }

    [Fact]
    public void Justo_al_cierre_ya_esta_fuera_y_justo_a_la_apertura_ya_esta_dentro()
    {
        var tramos = LunesAViernes();

        Assert.False(CalendarioLaboral.EstaEnHorario(tramos, Lima(14, 18)));
        Assert.True(CalendarioLaboral.EstaEnHorario(tramos, Lima(14, 9)));
    }

    [Fact]
    public void Los_minutos_habiles_del_viernes_por_la_tarde_al_lunes_por_la_manana_son_dos_horas()
    {
        // Viernes 17:00 a lunes 10:00: una hora el viernes y una el lunes. El fin de semana no cuenta.
        var tramos = LunesAViernes();

        Assert.Equal(120, CalendarioLaboral.MinutosHabilesEntre(tramos, Lima(18, 17), Lima(21, 10)));
    }

    [Fact]
    public void Sin_tramos_los_minutos_habiles_son_de_reloj_corrido()
    {
        Assert.Equal(90, CalendarioLaboral.MinutosHabilesEntre([], Lima(18, 20), Lima(18, 21, 30)));
    }

    [Fact]
    public void Describir_agrupa_los_dias_consecutivos_con_la_misma_jornada()
    {
        Assert.Equal("lunes a viernes de 09:00 a 18:00", CalendarioLaboral.Describir(LunesAViernes()));
    }

    /// <summary>
    /// FUN-07 (A1): el vencimiento de una transferencia se cuenta en horas hábiles. Dos horas pedidas
    /// el viernes a las 17:00 vencen el lunes por la mañana, no el sábado a las 19:00, cuando no hay
    /// nadie que pueda responder.
    /// </summary>
    [Fact]
    public void Sumar_minutos_habiles_salta_el_fin_de_semana()
    {
        // Viernes 18 a las 17:00: queda una hora de jornada, y la otra sale del lunes.
        var vencimiento = CalendarioLaboral.SumarMinutosHabiles(LunesAViernes(), Lima(18, 17), 120);

        Assert.Equal(Lima(21, 10), vencimiento);
    }

    [Fact]
    public void Dentro_de_la_jornada_suma_como_el_reloj()
    {
        var lunes10 = Lima(14, 10);

        Assert.Equal(lunes10.AddHours(2), CalendarioLaboral.SumarMinutosHabiles(LunesAViernes(), lunes10, 120));
    }

    /// <summary>Pedida fuera de hora, el plazo empieza a correr recién en la próxima apertura.</summary>
    [Fact]
    public void Fuera_de_hora_el_plazo_empieza_en_la_proxima_apertura()
    {
        var vencimiento = CalendarioLaboral.SumarMinutosHabiles(LunesAViernes(), Lima(19, 15), 60);

        Assert.Equal(Lima(21, 10), vencimiento);
    }

    /// <summary>Sin horario cargado no hay jornada que respetar: se suma a reloj corrido.</summary>
    [Fact]
    public void Sin_tramos_suma_a_reloj_corrido()
    {
        var sabado = Lima(19, 15);

        Assert.Equal(sabado.AddMinutes(90), CalendarioLaboral.SumarMinutosHabiles([], sabado, 90));
    }
}
