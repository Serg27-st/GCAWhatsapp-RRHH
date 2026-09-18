using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Servicios;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// T2.13 (ARQ-07): lo que la fabrica deja en el contexto. Se prueba aca y no en cada regla porque es
/// el punto donde una lectura mal armada se propaga a las veinte: las reglas no consultan la base.
/// </summary>
public class FabricaContextoReglaTests : IDisposable
{
    private const int CuentaId = 7;
    private const int OtraCuentaId = 8;
    private const int TitularId = 10;

    /// <summary>Lunes 14 de setiembre de 2026, 10:00 en Lima (15:00 UTC): dentro del horario sembrado.</summary>
    private static readonly DateTimeOffset Lunes10 = new(2026, 9, 14, 15, 0, 0, TimeSpan.Zero);

    private readonly RrhhDbContext _db;
    private readonly FakeTimeProvider _reloj = new(Lunes10);
    private readonly FabricaContextoRegla _fabrica;

    public FabricaContextoReglaTests()
    {
        _db = new RrhhDbContext(new DbContextOptionsBuilder<RrhhDbContext>()
            .UseInMemoryDatabase($"fabrica-{Guid.NewGuid()}")
            .Options);

        _db.Database.EnsureCreated();
        Sembrar();

        _fabrica = new FabricaContextoRegla(
            _db,
            new ConfiguracionReglasService(_db, new MemoryCache(new MemoryCacheOptions()), _reloj),
            new HorarioAtencionService(_db, _reloj, NullLogger<HorarioAtencionService>.Instance),
            new AusenciaService(_db, _reloj),
            new CuentaService(_db, new AlertaOperativaService(_db, _reloj), _reloj),
            _reloj);
    }

    private DateTime Ahora => _reloj.GetUtcNow().UtcDateTime;

    private void Sembrar()
    {
        _db.Cuentas.AddRange(
            new Cuenta { CuentaId = CuentaId, Nombre = "Alicorp", Activo = true },
            new Cuenta { CuentaId = OtraCuentaId, Nombre = "Intradevco", Activo = true });

        _db.Analistas.Add(new Analista
        {
            AnalistaId = TitularId, Nombre = "Ana Torres", Email = "ana@gca.pe", Activo = true
        });

        _db.AnalistaCuentas.Add(new AnalistaCuenta
        {
            AnalistaCuentaId = 1, AnalistaId = TitularId, CuentaId = CuentaId, EsBackup = false
        });

        _db.Hcs.AddRange(
            new Hc { HcId = 1, CuentaId = CuentaId, Titulo = "Operario", Estado = EstadoHc.Abierta, FechaCreacion = Ahora },
            new Hc { HcId = 2, CuentaId = OtraCuentaId, Titulo = "Almacenero", Estado = EstadoHc.Abierta, FechaCreacion = Ahora });

        // Lunes a viernes de 09:00 a 18:00 en Lima, que es UTC-5.
        for (var dia = DayOfWeek.Monday; dia <= DayOfWeek.Friday; dia++)
        {
            _db.HorariosAtencion.Add(new HorarioAtencion
            {
                HorarioId = (int)dia,
                CuentaId = null,
                DiaSemana = dia,
                HoraInicio = new TimeOnly(9, 0),
                HoraFin = new TimeOnly(18, 0)
            });
        }

        _db.SaveChanges();
    }

    private Conversacion CrearConversacion(
        int? postulanteId = null, int? cuentaContextoId = null, EstadoConversacion estado = EstadoConversacion.EnMenuBot)
    {
        var conversacion = new Conversacion
        {
            ConversacionId = 100,
            TelefonoE164 = "+51987654321",
            PostulanteId = postulanteId,
            CuentaContextoId = cuentaContextoId,
            Estado = estado,
            FechaCreacion = Ahora.AddDays(-10),
            FechaUltimaActividad = Ahora,
            FechaUltimoMensajeEntrante = Ahora
        };

        _db.Conversaciones.Add(conversacion);
        _db.SaveChanges();

        return conversacion;
    }

    private int CrearPostulanteConProcesos(params (int HcId, int CuentaId, EstadoPostulacion Estado)[] procesos)
    {
        _db.Postulantes.Add(new Postulante
        {
            PostulanteId = 50, Dni = "45678912", NombreCompleto = "Maria Quispe", FechaRegistro = Ahora
        });

        var id = 1;

        foreach (var (hcId, cuentaId, estado) in procesos)
        {
            _db.Postulaciones.Add(new Postulacion
            {
                PostulacionId = id++,
                PostulanteId = 50,
                HcId = hcId,
                CuentaId = cuentaId,
                AnalistaAsignadoId = TitularId,
                EtapaKanbanId = 1,
                Estado = estado,
                FechaCreacion = Ahora.AddDays(-5),
                FechaUltimaActividad = Ahora.AddDays(-1)
            });
        }

        _db.SaveChanges();
        return 50;
    }

    private Task<ContextoRegla> EntranteAsync(string? idBoton = null, DateTime? actividadAnterior = null) =>
        _fabrica.ParaMensajeEntranteAsync(100, 0, idBoton, actividadAnterior, Guid.NewGuid(), "prueba");

    /// <summary>A3: la desambiguacion necesita nombrarle al postulante sus procesos, no solo contarlos.</summary>
    [Fact]
    public async Task Carga_las_postulaciones_del_postulante_con_su_cuenta_y_su_vacante()
    {
        var postulanteId = CrearPostulanteConProcesos(
            (1, CuentaId, EstadoPostulacion.EnProceso),
            (2, OtraCuentaId, EstadoPostulacion.Descartado));

        CrearConversacion(postulanteId);

        var ctx = await EntranteAsync();

        Assert.Equal(2, ctx.PostulacionesDelPostulante.Count);

        var viva = ctx.PostulacionesDelPostulante.Single(p => p.Estado == EstadoPostulacion.EnProceso);

        Assert.Equal("Alicorp", viva.Cuenta);
        Assert.Equal("Operario", viva.Vacante);
        Assert.Equal(TitularId, viva.AnalistaAsignadoId);
        Assert.Equal([CuentaId], ctx.CuentasVivas);
    }

    [Fact]
    public async Task Sin_postulante_no_hay_postulaciones_que_cargar()
    {
        CrearConversacion();

        Assert.Empty((await EntranteAsync()).PostulacionesDelPostulante);
    }

    /// <summary>FUN-03: navegar el menu no es elegir; la Regla 19 lo distingue por la pagina.</summary>
    [Fact]
    public async Task El_boton_de_pagina_no_es_una_eleccion()
    {
        CrearConversacion();

        var ctx = await EntranteAsync(IdsBoton.ParaPagina(3));

        Assert.Equal(3, ctx.PaginaMenu);
        Assert.Equal(OrigenEleccion.Ninguna, ctx.OrigenEleccion);
    }

    [Fact]
    public async Task El_boton_de_proceso_llega_como_postulacion_elegida()
    {
        CrearConversacion();

        Assert.Equal(41, (await EntranteAsync(IdsBoton.ParaProceso(41))).PostulacionElegidaId);
    }

    [Theory]
    [InlineData("cuenta_7")]
    [InlineData("hc_1")]
    public async Task Un_boton_de_cuenta_o_de_vacante_es_una_eleccion_del_postulante(string idBoton)
    {
        CrearConversacion();

        Assert.Equal(OrigenEleccion.Boton, (await EntranteAsync(idBoton)).OrigenEleccion);
    }

    /// <summary>AL2: heredar la cuenta de un mensaje anterior no es lo mismo que elegirla ahora.</summary>
    [Fact]
    public async Task La_cuenta_que_ya_estaba_en_el_hilo_es_origen_Contexto()
    {
        CrearConversacion(cuentaContextoId: CuentaId);

        var ctx = await EntranteAsync();

        Assert.Equal(OrigenEleccion.Contexto, ctx.OrigenEleccion);
        Assert.Equal(CuentaId, ctx.Cuenta?.CuentaId);
    }

    [Fact]
    public async Task Sin_cuenta_ni_boton_nadie_eligio_nada()
    {
        CrearConversacion();

        Assert.Equal(OrigenEleccion.Ninguna, (await EntranteAsync()).OrigenEleccion);
    }

    /// <summary>FUN-04: la Regla 3 avisa una vez por periodo y dice cuando se retoma.</summary>
    [Fact]
    public async Task Fuera_de_horario_el_contexto_trae_el_cierre_anterior_y_la_proxima_apertura()
    {
        CrearConversacion();

        // Viernes 18 de setiembre 21:00 en Lima (sabado 02:00 UTC): la jornada cerro a las 18:00.
        _reloj.SetUtcNow(new DateTimeOffset(2026, 9, 19, 2, 0, 0, TimeSpan.Zero));

        var ctx = await EntranteAsync();

        Assert.False(ctx.DentroDeHorario);
        Assert.Equal(new DateTime(2026, 9, 18, 23, 0, 0, DateTimeKind.Utc), ctx.InicioPeriodoFueraHorario);
        Assert.Equal(new DateTime(2026, 9, 21, 14, 0, 0, DateTimeKind.Utc), ctx.ProximaApertura);
    }

    [Fact]
    public async Task Dentro_de_horario_no_hay_periodo_fuera_de_horario()
    {
        CrearConversacion();

        Assert.Null((await EntranteAsync()).InicioPeriodoFueraHorario);
    }

    /// <summary>FUN-05 y FUN-06: los plazos de Jefatura corren en horas habiles, no a reloj corrido.</summary>
    [Fact]
    public async Task Los_plazos_de_seguimiento_se_miden_en_minutos_habiles()
    {
        var conversacion = CrearConversacion(estado: EstadoConversacion.Escalada);

        // Viernes 18 a las 17:00 de Lima: queda una hora habil antes del cierre.
        var viernes17 = new DateTime(2026, 9, 18, 22, 0, 0, DateTimeKind.Utc);
        conversacion.FechaEscalamiento = viernes17;
        conversacion.FechaPendienteDesde = viernes17;
        conversacion.FechaTextoNoReconocido = viernes17;
        _db.SaveChanges();

        // Lunes 21 a las 10:00 de Lima: esa hora del viernes mas una del lunes.
        _reloj.SetUtcNow(new DateTimeOffset(2026, 9, 21, 15, 0, 0, TimeSpan.Zero));

        var ctx = await EntranteAsync();

        Assert.Equal(120, ctx.MinutosHabilesDesdeEscalamiento);
        Assert.Equal(120, ctx.MinutosHabilesEnPendiente);
        Assert.Equal(120, ctx.MinutosHabilesDesdeTextoNoReconocido);
    }

    [Fact]
    public async Task Sin_sellos_de_seguimiento_no_hay_minutos_que_medir()
    {
        CrearConversacion();

        var ctx = await EntranteAsync();

        Assert.Null(ctx.MinutosHabilesDesdeEscalamiento);
        Assert.Null(ctx.MinutosHabilesEnPendiente);
        Assert.Null(ctx.MinutosHabilesDesdeTextoNoReconocido);
    }

    /// <summary>FUN-07: la Regla 8 vence la transferencia no urgente que nadie respondio.</summary>
    [Fact]
    public async Task La_transferencia_pendiente_viaja_en_el_contexto()
    {
        CrearConversacion();

        _db.Transferencias.AddRange(
            new Transferencia
            {
                TransferenciaId = 1, ConversacionId = 100, AnalistaOrigenId = TitularId, AnalistaDestinoId = 11,
                Estado = EstadoTransferencia.Rechazada, Fecha = Ahora.AddDays(-2)
            },
            new Transferencia
            {
                TransferenciaId = 2, ConversacionId = 100, AnalistaOrigenId = TitularId, AnalistaDestinoId = 11,
                Estado = EstadoTransferencia.Pendiente, Fecha = Ahora.AddHours(-1),
                FechaVencimiento = Ahora.AddHours(1)
            });

        _db.SaveChanges();

        Assert.Equal(2, (await EntranteAsync()).TransferenciaPendiente?.TransferenciaId);
    }

    [Fact]
    public async Task La_instantanea_del_webhook_llega_tal_cual()
    {
        CrearConversacion();

        Assert.Equal(4, (await EntranteAsync(actividadAnterior: Ahora.AddDays(-4))).DiasDesdeActividadAnterior);
    }

    /// <summary>FUN-10 y FUN-11: el barrido por postulacion necesita su hilo para poder responderle.</summary>
    [Fact]
    public async Task El_contexto_por_postulacion_carga_la_postulacion_su_hilo_y_su_cuenta()
    {
        var postulanteId = CrearPostulanteConProcesos((1, CuentaId, EstadoPostulacion.Descartado));
        CrearConversacion(postulanteId);

        var ctx = await _fabrica.ParaTiempoPostulacionAsync(1, "barrido:post:1");

        Assert.Equal(TipoDisparador.TiempoTranscurridoPostulacion, ctx.Disparador);
        Assert.Equal(1, ctx.Postulacion?.PostulacionId);
        Assert.Equal(100, ctx.Conversacion?.ConversacionId);
        Assert.Equal(CuentaId, ctx.Cuenta?.CuentaId);
        Assert.Equal(1, ctx.Hc?.HcId);
    }

    public void Dispose() => _db.Dispose();
}
