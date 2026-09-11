using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Servicios;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// Horario de atencion (Regla 3) y el conteo de minutos habiles del que depende el escalamiento
/// de la Regla 2.
/// </summary>
public class HorarioAtencionServiceTests : IDisposable
{
    private readonly RrhhDbContext _db;
    private readonly HorarioAtencionService _servicio;

    public HorarioAtencionServiceTests()
    {
        var opciones = new DbContextOptionsBuilder<RrhhDbContext>()
            .UseInMemoryDatabase($"horario-{Guid.NewGuid()}")
            .Options;

        _db = new RrhhDbContext(opciones);
        _db.Database.EnsureCreated();

        _servicio = new HorarioAtencionService(_db, NullLogger<HorarioAtencionService>.Instance);
    }

    private static HorarioAtencion Tramo(DayOfWeek dia, string inicio, string fin, int? cuentaId = null) =>
        new()
        {
            CuentaId = cuentaId,
            DiaSemana = dia,
            HoraInicio = TimeOnly.Parse(inicio),
            HoraFin = TimeOnly.Parse(fin)
        };

    private async Task SembrarJornadaAsync(string inicio = "09:00", string fin = "18:00")
    {
        DayOfWeek[] laborables =
            [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

        _db.HorariosAtencion.AddRange(laborables.Select(d => Tramo(d, inicio, fin)));
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Rechaza_un_tramo_que_termina_antes_de_empezar()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _servicio.ReemplazarTramosAsync(null, [Tramo(DayOfWeek.Monday, "18:00", "09:00")], analistaId: 1));

        Assert.Contains("termina antes de empezar", ex.Message);
    }

    [Fact]
    public async Task Rechaza_tramos_superpuestos_el_mismo_dia()
    {
        // Importa porque los minutos habiles se suman tramo por tramo: la franja compartida se
        // contaria dos veces y la Regla 2 escalaria antes de tiempo.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _servicio.ReemplazarTramosAsync(
                null,
                [Tramo(DayOfWeek.Monday, "09:00", "14:00"), Tramo(DayOfWeek.Monday, "13:00", "18:00")],
                analistaId: 1));

        Assert.Contains("se superponen", ex.Message);
    }

    [Fact]
    public async Task Acepta_dos_tramos_del_mismo_dia_que_no_se_tocan()
    {
        // El caso real del refrigerio: manana y tarde partidas.
        await _servicio.ReemplazarTramosAsync(
            null,
            [Tramo(DayOfWeek.Monday, "09:00", "13:00"), Tramo(DayOfWeek.Monday, "14:00", "18:00")],
            analistaId: 1);

        Assert.Equal(2, (await _servicio.ObtenerTramosAsync(null)).Count);
    }

    [Fact]
    public async Task Acepta_el_mismo_horario_en_dias_distintos()
    {
        await _servicio.ReemplazarTramosAsync(
            null,
            [Tramo(DayOfWeek.Monday, "09:00", "18:00"), Tramo(DayOfWeek.Tuesday, "09:00", "18:00")],
            analistaId: 1);

        Assert.Equal(2, (await _servicio.ObtenerTramosAsync(null)).Count);
    }

    [Fact]
    public async Task Reemplazar_borra_los_tramos_anteriores()
    {
        await _servicio.ReemplazarTramosAsync(null, [Tramo(DayOfWeek.Monday, "09:00", "18:00")], analistaId: 1);
        await _servicio.ReemplazarTramosAsync(null, [Tramo(DayOfWeek.Friday, "10:00", "16:00")], analistaId: 1);

        var tramos = await _servicio.ObtenerTramosAsync(null);

        Assert.Equal(DayOfWeek.Friday, Assert.Single(tramos).DiaSemana);
    }

    [Fact]
    public async Task Un_mensaje_de_media_tarde_de_un_miercoles_esta_dentro_de_jornada()
    {
        await SembrarJornadaAsync();

        // 2026-08-19 es miercoles. 20:00 UTC son las 15:00 en Lima.
        var momento = new DateTime(2026, 8, 19, 20, 0, 0, DateTimeKind.Utc);

        Assert.True(await _servicio.EstaEnHorarioAsync(null, momento));
    }

    [Fact]
    public async Task Un_mensaje_de_madrugada_queda_fuera_de_jornada()
    {
        await SembrarJornadaAsync();

        // 07:00 UTC son las 02:00 en Lima.
        var momento = new DateTime(2026, 8, 19, 7, 0, 0, DateTimeKind.Utc);

        Assert.False(await _servicio.EstaEnHorarioAsync(null, momento));
    }

    [Fact]
    public async Task Un_domingo_queda_fuera_de_jornada()
    {
        await SembrarJornadaAsync();

        // 2026-08-23 es domingo, a las 15:00 de Lima.
        var momento = new DateTime(2026, 8, 23, 20, 0, 0, DateTimeKind.Utc);

        Assert.False(await _servicio.EstaEnHorarioAsync(null, momento));
    }

    [Fact]
    public async Task Sin_horario_configurado_se_asume_dentro_de_jornada()
    {
        // La Regla 3 solo manda un aviso informativo: mandarlo de mas es ruido que sube los
        // reportes de spam descritos en la Seccion 2.4.
        Assert.True(await _servicio.EstaEnHorarioAsync(null, DateTime.UtcNow));
    }

    [Fact]
    public async Task Los_minutos_habiles_ignoran_la_noche()
    {
        await SembrarJornadaAsync();

        // Miercoles 17:30 Lima (22:30 UTC) hasta jueves 09:30 Lima (14:30 UTC): 16 horas de reloj,
        // pero solo 30 minutos antes del cierre mas 30 despues de abrir.
        var desde = new DateTime(2026, 8, 19, 22, 30, 0, DateTimeKind.Utc);
        var hasta = new DateTime(2026, 8, 20, 14, 30, 0, DateTimeKind.Utc);

        Assert.Equal(60, await _servicio.MinutosHabilesEntreAsync(null, desde, hasta), precision: 0);
        Assert.Equal(960, (hasta - desde).TotalMinutes, precision: 0);
    }

    [Fact]
    public async Task Un_fin_de_semana_completo_no_suma_minutos_habiles()
    {
        await SembrarJornadaAsync();

        // Sabado 2026-08-22 a domingo 2026-08-23, ambos en horario de oficina de Lima.
        var desde = new DateTime(2026, 8, 22, 15, 0, 0, DateTimeKind.Utc);
        var hasta = new DateTime(2026, 8, 23, 20, 0, 0, DateTimeKind.Utc);

        Assert.Equal(0, await _servicio.MinutosHabilesEntreAsync(null, desde, hasta), precision: 0);
    }

    [Fact]
    public async Task Sin_horario_configurado_los_minutos_habiles_caen_a_reloj_corrido()
    {
        // Preferible escalar de mas que dejar una conversacion sin atender porque nadie cargo la jornada.
        var desde = new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc);
        var hasta = desde.AddHours(3);

        Assert.Equal(180, await _servicio.MinutosHabilesEntreAsync(null, desde, hasta), precision: 0);
    }

    [Fact]
    public async Task El_horario_propio_de_la_cuenta_gana_sobre_el_general()
    {
        await SembrarJornadaAsync();

        _db.HorariosAtencion.Add(Tramo(DayOfWeek.Wednesday, "09:00", "12:00", cuentaId: 1));
        await _db.SaveChangesAsync();

        // Miercoles 15:00 Lima: dentro del general (hasta 18:00) pero fuera del de la cuenta 1.
        var momento = new DateTime(2026, 8, 19, 20, 0, 0, DateTimeKind.Utc);

        Assert.True(await _servicio.EstaEnHorarioAsync(null, momento));
        Assert.False(await _servicio.EstaEnHorarioAsync(1, momento));
    }

    [Fact]
    public async Task La_descripcion_agrupa_los_dias_consecutivos_en_un_rango()
    {
        await SembrarJornadaAsync();

        Assert.Equal("lunes a viernes de 09:00 a 18:00", await _servicio.DescribirHorarioAsync(null));
    }

    [Fact]
    public async Task La_descripcion_de_una_jornada_partida_no_repite_el_dia()
    {
        // Este texto va como parametro de la plantilla de la Regla 3: lo lee el postulante.
        _db.HorariosAtencion.AddRange(
            Tramo(DayOfWeek.Monday, "09:00", "13:00"),
            Tramo(DayOfWeek.Monday, "14:00", "18:00"));

        await _db.SaveChangesAsync();

        Assert.Equal(
            "lunes de 09:00 a 13:00 y de 14:00 a 18:00",
            await _servicio.DescribirHorarioAsync(null));
    }

    [Fact]
    public async Task La_descripcion_agrupa_los_dias_que_comparten_una_jornada_partida()
    {
        DayOfWeek[] laborables =
            [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

        _db.HorariosAtencion.AddRange(laborables.Select(d => Tramo(d, "09:00", "13:00")));
        _db.HorariosAtencion.AddRange(laborables.Select(d => Tramo(d, "14:00", "18:00")));
        await _db.SaveChangesAsync();

        Assert.Equal(
            "lunes a viernes de 09:00 a 13:00 y de 14:00 a 18:00",
            await _servicio.DescribirHorarioAsync(null));
    }

    [Fact]
    public async Task La_descripcion_separa_los_dias_con_jornadas_distintas()
    {
        _db.HorariosAtencion.AddRange(
            Tramo(DayOfWeek.Monday, "09:00", "18:00"),
            Tramo(DayOfWeek.Saturday, "09:00", "13:00"));

        await _db.SaveChangesAsync();

        Assert.Equal(
            "lunes de 09:00 a 18:00, sabado de 09:00 a 13:00",
            await _servicio.DescribirHorarioAsync(null));
    }

    public void Dispose() => _db.Dispose();
}
