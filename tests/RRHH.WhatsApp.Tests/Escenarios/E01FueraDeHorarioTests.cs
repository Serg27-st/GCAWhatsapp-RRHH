namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// E01 parte 2 (FUN-04, COR-01, C1): quien escribe fuera de hora recibe **un** aviso por periodo, y
/// ese aviso dice cuando se retoma la atencion.
/// <para>
/// La R3 anterior media el periodo con las 8 horas desde el ultimo entrante: tres mensajes seguidos
/// un viernes a la noche no avisaban nunca, y el aviso no decia cuando volvian a responder.
/// </para>
/// </summary>
public class E01FueraDeHorarioTests : IDisposable
{
    private readonly ArnesEscenario _arnes = new();

    /// <summary>Del lunes 10:00 de Lima (inicio del reloj) al viernes 20:00, ya cerrada la jornada.</summary>
    private static readonly TimeSpan HastaElViernesALaNoche = TimeSpan.FromDays(4) + TimeSpan.FromHours(10);

    private IEnumerable<string> Avisos() =>
        _arnes.Enviados().Where(e => e.Detalle.Contains("horario de atencion")).Select(e => e.Detalle);

    [Fact]
    public async Task Tres_mensajes_el_viernes_a_la_noche_dan_un_solo_aviso_que_dice_cuando_se_retoma()
    {
        await _arnes.ConHorarioComercialAsync();

        await _arnes.ConversarAsync(
            new Avanzar(HastaElViernesALaNoche),
            new Entrante("Hola, hay trabajo?"),
            new Entrante("Alguien ahi?"),
            new Entrante("Buenas noches"),
            new ConsumirOutbox(),
            new Despachar());

        var aviso = Assert.Single(Avisos());

        Assert.Contains("lunes", aviso);
        Assert.Contains("09:00", aviso);
    }

    [Fact]
    public async Task El_periodo_siguiente_vuelve_a_avisar()
    {
        await _arnes.ConHorarioComercialAsync();

        await _arnes.ConversarAsync(
            new Avanzar(HastaElViernesALaNoche),
            new Entrante("Hola, hay trabajo?"),
            new ConsumirOutbox(),
            new Despachar(),
            // Lunes 19:00 de Lima: la jornada del lunes ya cerro, es otro periodo fuera de horario.
            new Avanzar(TimeSpan.FromDays(2) + TimeSpan.FromHours(23)),
            new Entrante("Sigo interesado"),
            new ConsumirOutbox(),
            new Despachar());

        Assert.Equal(2, Avisos().Count());
    }

    [Fact]
    public async Task Dentro_del_horario_no_se_avisa_nada()
    {
        await _arnes.ConHorarioComercialAsync();

        await _arnes.ConversarAsync(
            new Entrante("Hola, hay trabajo?"),
            new ConsumirOutbox(),
            new Despachar());

        Assert.Empty(Avisos());
    }

    public void Dispose() => _arnes.Dispose();
}
