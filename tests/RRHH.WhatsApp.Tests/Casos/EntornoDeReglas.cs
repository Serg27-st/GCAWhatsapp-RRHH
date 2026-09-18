using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Application.Reglas;
using RRHH.WhatsApp.Application.Reglas.Implementaciones;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Domain.Reglas;
using RRHH.WhatsApp.Infrastructure.Almacenamiento;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Proveedores;
using RRHH.WhatsApp.Infrastructure.Servicios;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// Arma el circuito completo con las implementaciones reales sobre una base en memoria: webhook,
/// outbox, fabrica de contexto, motor y ejecutor. Es el mismo grafo que cablea
/// <c>AgregarInfraestructura</c>, montado a mano para poder mirar la base entre paso y paso.
/// </summary>
internal sealed class EntornoDeReglas : IDisposable
{
    public const int CuentaId = 7;
    public const int TitularId = 10;
    public const int RespaldoId = 11;

    public RrhhDbContext Db { get; }
    public ProveedorSimulado Proveedor { get; }
    public RecepcionWebhook Recepcion { get; }
    public ProcesadorOutbox Procesador { get; }
    public BarridoTiempo Barrido { get; }
    public IEventoSistemaService Eventos { get; }
    public IConversacionService Conversaciones { get; }
    public IJobFormsInvitacionService Invitaciones { get; }
    public IPostulacionService Postulaciones { get; }
    public IPostulanteService Postulantes { get; }
    public IJobFormsService Formularios { get; }
    public RecepcionJobForms RecepcionFormulario { get; }
    public EnvioAnalista Envio { get; }
    public AccionesBandeja Bandeja { get; }
    public ICuentaService Cuentas { get; }
    public IAnalistaService Analistas { get; }
    public IUnidadTrabajo Unidad { get; }
    public DespachoEnvios Despacho { get; }
    public IMensajeService Mensajes { get; }
    public string CarpetaCv { get; }

    /// <summary>V33: la subcarpeta de los CVs, como en produccion cuando no se configura otra.</summary>
    public string CarpetaAdjuntos => Path.Combine(CarpetaCv, "adjuntos");

    public IAlmacenamientoAdjuntos AlmacenamientoAdjuntos { get; }
    public DescargaAdjuntos DescargaAdjuntos { get; }
    public PurgaAdjuntos PurgaAdjuntos { get; }

    /// <summary>La cadencia por defecto del Worker (appsettings), para que el arnes baje como en produccion.</summary>
    public static readonly ParametrosDescargaAdjuntos ParametrosDescarga = new(
        TamanoLote: 10,
        IntentosMaximos: 5,
        EsperaBase: TimeSpan.FromMinutes(1),
        TiempoMaximoPorArchivo: TimeSpan.FromMinutes(5));

    /// <summary>
    /// Lunes 14 de setiembre de 2026, 10:00 en Lima (15:00 UTC). Dentro del horario laboral a
    /// proposito: una prueba que no va sobre la Regla 3 no deberia depender de a que hora se corre.
    /// </summary>
    public static readonly DateTimeOffset InicioReloj = new(2026, 9, 14, 15, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// El reloj de todo el circuito (ARQ-01, ARQ-12). Lo mueve la prueba con <see cref="AvanzarAsync"/>:
    /// las reglas por tiempo —R2, R9, R16— se prueban avanzando horas en vez de reescribir fechas.
    /// </summary>
    public FakeTimeProvider Reloj { get; } = new(InicioReloj);

    /// <summary>Instante actual del reloj simulado, en UTC como lo guarda la base.</summary>
    public DateTime Ahora => Reloj.GetUtcNow().UtcDateTime;

    /// <param name="decorarAuditoria">
    /// Envuelve la auditoria real, para inyectar un fallo en medio de un evento (E16): es la ultima
    /// accion de varias reglas, y fallar ahi deja ya encolado el mensaje de la accion anterior.
    /// </param>
    public EntornoDeReglas(Func<IAuditoriaService, IAuditoriaService>? decorarAuditoria = null)
    {
        var opciones = new DbContextOptionsBuilder<RrhhDbContext>()
            .UseInMemoryDatabase($"reglas-{Guid.NewGuid()}")
            // V28: los casos de uso abren transacciones y EF InMemory no las soporta; sin esto la
            // advertencia se vuelve excepcion. Lo transaccional se prueba contra SQL Server.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        Db = new RrhhDbContext(opciones);
        Db.Database.EnsureCreated();

        Sembrar();

        var reloj = Reloj;

        Unidad = new UnidadTrabajoEf(Db);

        Proveedor = new ProveedorSimulado(reloj, NullLogger<ProveedorSimulado>.Instance);

        var configuracion = new ConfiguracionReglasService(Db, new MemoryCache(new MemoryCacheOptions()), reloj);
        var horarios = new HorarioAtencionService(Db, reloj, NullLogger<HorarioAtencionService>.Instance);
        var ausencias = new AusenciaService(Db, reloj);
        var cuentas = new CuentaService(Db, new AlertaOperativaService(Db, reloj), reloj);
        Cuentas = cuentas;
        var plantillas = new PlantillaService(Db, reloj, NullLogger<PlantillaService>.Instance);
        IAuditoriaService auditoria = new AuditoriaService(Db, reloj);
        auditoria = decorarAuditoria?.Invoke(auditoria) ?? auditoria;

        Invitaciones = new JobFormsInvitacionService(Db, reloj, NullLogger<JobFormsInvitacionService>.Instance);
        Postulaciones = new PostulacionService(Db, reloj, NullLogger<PostulacionService>.Instance);

        CarpetaCv = Path.Combine(Path.GetTempPath(), $"cv-prueba-{Guid.NewGuid():N}");

        var almacenamiento = new AlmacenamientoCvLocal(
            Options.Create(new OpcionesCv { Carpeta = CarpetaCv }),
            new EscanerDesactivado(NullLogger<EscanerDesactivado>.Instance),
            reloj,
            NullLogger<AlmacenamientoCvLocal>.Instance);

        Formularios = new JobFormsService(
            Db, almacenamiento, configuracion, reloj, NullLogger<JobFormsService>.Instance);

        var mensajes = new MensajeService(Db, reloj, NullLogger<MensajeService>.Instance);
        Mensajes = mensajes;

        AlmacenamientoAdjuntos = new AlmacenamientoAdjuntosLocal(
            Options.Create(new OpcionesAdjuntos()),
            Options.Create(new OpcionesCv { Carpeta = CarpetaCv }),
            new EscanerDesactivado(NullLogger<EscanerDesactivado>.Instance),
            reloj,
            NullLogger<AlmacenamientoAdjuntosLocal>.Instance);

        DescargaAdjuntos = new DescargaAdjuntos(
            mensajes, Proveedor, AlmacenamientoAdjuntos, reloj, NullLogger<DescargaAdjuntos>.Instance);

        Conversaciones = ServiciosDePrueba.Conversaciones(Db, reloj);
        Eventos = new EventoSistemaService(Db, reloj, NullLogger<EventoSistemaService>.Instance);

        // FUN-19: dar de baja a alguien mueve su cartera, asi que necesita el servicio de conversaciones.
        Analistas = new AnalistaService(
            Db, Conversaciones, new AlertaOperativaService(Db, reloj), Unidad, reloj);

        // FUN-16: la anonimizacion alcanza al hilo, sus mensajes, sus archivos, la outbox y la auditoria,
        // asi que el servicio se arma recien cuando todos esos colaboradores existen.
        Postulantes = new PostulanteService(
            Db, almacenamiento, AlmacenamientoAdjuntos, Conversaciones, mensajes, Eventos,
            auditoria, Unidad, reloj, NullLogger<PostulanteService>.Instance);

        Despacho = new DespachoEnvios(
            mensajes, Conversaciones, plantillas, Proveedor, new ValidadorEnvio(Conversaciones, plantillas),
            configuracion, reloj, NullLogger<DespachoEnvios>.Instance);

        var fabrica = new FabricaContextoRegla(Db, configuracion, horarios, ausencias, cuentas, reloj);

        var motor = new MotorReglas(ReglasEnOrden(), NullLogger<MotorReglas>.Instance);

        var ejecutor = new EjecutorAcciones(
            Conversaciones, mensajes, plantillas, Invitaciones, Postulaciones,
            Eventos, auditoria, new AlertaOperativaService(Db, reloj), cuentas, Analistas,
            NullLogger<EjecutorAcciones>.Instance);

        var evaluador = new EvaluadorReglas(motor, ejecutor, NullLogger<EvaluadorReglas>.Instance);

        Recepcion = new RecepcionWebhook(
            Proveedor, Conversaciones, mensajes, Eventos, Unidad, NullLogger<RecepcionWebhook>.Instance);

        RecepcionFormulario = new RecepcionJobForms(
            Invitaciones, Formularios, Postulantes, Postulaciones, Conversaciones, Eventos, Unidad,
            NullLogger<RecepcionJobForms>.Instance);

        Envio = new EnvioAnalista(
            Conversaciones, mensajes, plantillas, Eventos, auditoria, fabrica, motor, Despacho, reloj,
            NullLogger<EnvioAnalista>.Instance);

        Bandeja = new AccionesBandeja(Postulaciones, Eventos, NullLogger<AccionesBandeja>.Instance);

        Procesador = new ProcesadorOutbox(
            fabrica, evaluador, Postulaciones, configuracion, NullLogger<ProcesadorOutbox>.Instance);
        Barrido = new BarridoTiempo(fabrica, evaluador, Unidad, reloj, NullLogger<BarridoTiempo>.Instance);

        PurgaAdjuntos = new PurgaAdjuntos(
            mensajes, AlmacenamientoAdjuntos, configuracion, NullLogger<PurgaAdjuntos>.Instance);
    }

    /// <summary>Las mismas reglas que registra AgregarReglas, en el mismo orden de prioridad.</summary>
    public static IReglaNegocio[] ReglasEnOrden() =>
    [
        new R15OptInYVentana(),
        new R16Reactivacion(),
        new R19FallbackMenu(),
        new R19DerivacionPorSilencio(),
        new R19AvisoPendiente(),
        new R06Desambiguacion(),
        new R09RepreguntaEmpresa(),
        new R14Ausencias(),
        new R08VencimientoTransferencia(),
        new R16Archivado(),
        new R16ArchivadoConversacion(),
        new R02Escalamiento(),
        new R02SegundoNivel(),
        new R01Asignacion(),
        new R20VacanteCerrada(),
        new R09EnvioLink(),
        new R09ConfirmacionJobForms(),
        new R09SeguimientoJobForms(),
        new R03FueraDeHorario(),
        new R12CierreCortesia()
    ];

    /// <summary>
    /// Una cuenta con vacante abierta, su analista titular y su respaldo fijo. El CuentaId 7
    /// coincide con el boton "cuenta_7" de los payloads de prueba.
    /// </summary>
    private void Sembrar()
    {
        Db.Cuentas.Add(new Cuenta { CuentaId = CuentaId, Nombre = "Alicorp", Activo = true });

        Db.Analistas.AddRange(
            new Analista { AnalistaId = TitularId, Nombre = "Ana Torres", Email = "ana@gca.pe", Activo = true },
            new Analista { AnalistaId = RespaldoId, Nombre = "Luis Vega", Email = "luis@gca.pe", Activo = true });

        Db.AnalistaCuentas.AddRange(
            new AnalistaCuenta { AnalistaCuentaId = 1, AnalistaId = TitularId, CuentaId = CuentaId, EsBackup = false },
            new AnalistaCuenta { AnalistaCuentaId = 2, AnalistaId = RespaldoId, CuentaId = CuentaId, EsBackup = true });

        Db.Hcs.Add(new Hc
        {
            HcId = 1,
            CuentaId = CuentaId,
            Titulo = "Operario de produccion",
            UrlJobForms = "https://forms.gle/operario",
            Estado = EstadoHc.Abierta,
            FechaCreacion = Ahora
        });

        Db.SaveChanges();
    }


    /// <summary>
    /// Ingresa un payload y deja el hilo con actividad al dia, que es como llega en produccion:
    /// el mensaje acaba de entrar.
    /// <para>
    /// Los payloads de prueba traen un timestamp fijo de agosto de 2025. Sin este ajuste el hilo
    /// nace con la ventana de 24h ya cerrada —y con edad de sobra para que la Regla 16 lo archive—,
    /// y eso tapa lo que la prueba quiere mirar.
    /// </para>
    /// </summary>
    public async Task IngresarAsync(string payload)
    {
        await Recepcion.ProcesarAsync(payload, new Dictionary<string, string>());

        var ahora = Ahora;

        foreach (var conversacion in await Db.Conversaciones.ToListAsync())
        {
            conversacion.FechaUltimaActividad = ahora;
            conversacion.FechaUltimoMensajeEntrante = ahora;
        }

        await Db.SaveChangesAsync();
    }
    /// <summary>
    /// Vacia la cola atendible, como haria una vuelta del ConsumidorOutbox.
    /// <para>
    /// Por defecto despacha despues, como hace el Worker a los pocos segundos (V29): las pruebas de
    /// reglas miran lo que el bot le manda al postulante, no la cola intermedia. El arnes de
    /// escenarios pasa <paramref name="despachar"/> en false para que el despacho sea un paso explicito.
    /// </para>
    /// </summary>
    public async Task<int> ConsumirOutboxAsync(bool despachar = true)
    {
        var pendientes = await Eventos.ObtenerPendientesAsync(50, ProcesadorOutbox.TiposQueAtiende);

        // Igual que ConsumidorOutbox (COR-04): cada evento con su marca de procesado, en una sola
        // transaccion. En memoria no se deshace nada; lo que se prueba aca es el orden y que un fallo
        // deja el evento pendiente para reintentarlo.
        foreach (var evento in pendientes)
        {
            await Unidad.EjecutarAsync(async c =>
            {
                await Procesador.ProcesarAsync(evento, c);
                await Eventos.MarcarProcesadoAsync(evento.EventoId, c);
            });
        }

        if (despachar)
            await DespacharAsync();

        return pendientes.Count;
    }

    /// <summary>Una vuelta del despachador de envios del Worker (V29).</summary>
    public Task<ResumenDespacho> DespacharAsync() => Despacho.ProcesarAsync(100, TimeSpan.FromMinutes(2));


    /// <summary>
    /// Pone la actividad del hilo al dia.
    /// <para>
    /// Los payloads de prueba traen un timestamp fijo de agosto de 2025, asi que una conversacion
    /// recien ingresada nace con mas de 90 dias de antiguedad y la Regla 16 la archiva antes de
    /// que la prueba pueda mirar otra cosa. Las pruebas que no van sobre el archivado empiezan
    /// por aca.
    /// </para>
    /// </summary>
    public async Task RefrescarActividadAsync(int conversacionId)
    {
        var conversacion = await Db.Conversaciones.FirstAsync(c => c.ConversacionId == conversacionId);

        conversacion.FechaUltimaActividad = Ahora;
        conversacion.FechaUltimoMensajeEntrante = Ahora;

        await Db.SaveChangesAsync();
    }

    /// <summary>
    /// Avanza el reloj de todo el circuito. Es asincrono porque el arnes de escenarios (T1.02) lo
    /// va a encadenar con los demas pasos de una conversacion.
    /// </summary>
    public Task AvanzarAsync(TimeSpan lapso)
    {
        Reloj.Advance(lapso);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Db.Dispose();

        if (Directory.Exists(CarpetaCv))
            Directory.Delete(CarpetaCv, recursive: true);
    }
}
