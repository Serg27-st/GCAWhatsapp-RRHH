using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
    public string CarpetaCv { get; }

    public EntornoDeReglas()
    {
        var opciones = new DbContextOptionsBuilder<RrhhDbContext>()
            .UseInMemoryDatabase($"reglas-{Guid.NewGuid()}")
            .Options;

        Db = new RrhhDbContext(opciones);
        Db.Database.EnsureCreated();

        Sembrar();

        // T0.10 reemplazara TimeProvider.System por un FakeTimeProvider expuesto como Reloj; por
        // ahora cada servicio recibe el reloj real, igual que en produccion.
        var reloj = TimeProvider.System;

        Proveedor = new ProveedorSimulado(reloj, NullLogger<ProveedorSimulado>.Instance);

        var configuracion = new ConfiguracionReglasService(Db, new MemoryCache(new MemoryCacheOptions()), reloj);
        var horarios = new HorarioAtencionService(Db, reloj, NullLogger<HorarioAtencionService>.Instance);
        var ausencias = new AusenciaService(Db);
        var cuentas = new CuentaService(Db, reloj);
        Cuentas = cuentas;
        var plantillas = new PlantillaService(Db, reloj, NullLogger<PlantillaService>.Instance);
        var auditoria = new AuditoriaService(Db, reloj);

        Invitaciones = new JobFormsInvitacionService(Db, reloj, NullLogger<JobFormsInvitacionService>.Instance);
        Postulaciones = new PostulacionService(Db, reloj, NullLogger<PostulacionService>.Instance);

        CarpetaCv = Path.Combine(Path.GetTempPath(), $"cv-prueba-{Guid.NewGuid():N}");

        var almacenamiento = new AlmacenamientoCvLocal(
            Options.Create(new OpcionesCv { Carpeta = CarpetaCv }),
            new EscanerDesactivado(NullLogger<EscanerDesactivado>.Instance),
            reloj,
            NullLogger<AlmacenamientoCvLocal>.Instance);

        Postulantes = new PostulanteService(Db, almacenamiento, reloj, NullLogger<PostulanteService>.Instance);

        Formularios = new JobFormsService(
            Db, almacenamiento, configuracion, reloj, NullLogger<JobFormsService>.Instance);

        var mensajes = new MensajeService(Db, reloj, NullLogger<MensajeService>.Instance);

        Conversaciones = new ConversacionService(Db, reloj, NullLogger<ConversacionService>.Instance);
        Eventos = new EventoSistemaService(Db, reloj, NullLogger<EventoSistemaService>.Instance);

        var fabrica = new FabricaContextoRegla(Db, configuracion, horarios, ausencias, reloj);

        // Las mismas reglas que registra AgregarReglas, en el mismo orden de prioridad.
        IReglaNegocio[] reglas =
        [
            new R15OptInYVentana(),
            new R19FallbackMenu(),
            new R09RepreguntaEmpresa(),
            new R14Ausencias(),
            new R16Archivado(),
            new R02Escalamiento(),
            new R01Asignacion(),
            new R20VacanteCerrada(),
            new R09EnvioLink(),
            new R09ConfirmacionJobForms(),
            new R09SeguimientoJobForms(),
            new R03FueraDeHorario(),
            new R12CierreCortesia()
        ];

        var motor = new MotorReglas(reglas, NullLogger<MotorReglas>.Instance);

        var ejecutor = new EjecutorAcciones(
            Proveedor, Conversaciones, mensajes, plantillas, Invitaciones, Postulaciones,
            Eventos, auditoria, cuentas,
            NullLogger<EjecutorAcciones>.Instance);

        var evaluador = new EvaluadorReglas(motor, ejecutor, NullLogger<EvaluadorReglas>.Instance);

        Recepcion = new RecepcionWebhook(
            Proveedor, Conversaciones, mensajes, Eventos, NullLogger<RecepcionWebhook>.Instance);

        RecepcionFormulario = new RecepcionJobForms(
            Invitaciones, Formularios, Postulantes, Postulaciones, Conversaciones, Eventos,
            NullLogger<RecepcionJobForms>.Instance);

        Envio = new EnvioAnalista(
            Proveedor, Conversaciones, mensajes, plantillas, Eventos, fabrica, motor,
            NullLogger<EnvioAnalista>.Instance);

        Bandeja = new AccionesBandeja(Postulaciones, Eventos, NullLogger<AccionesBandeja>.Instance);

        Procesador = new ProcesadorOutbox(fabrica, evaluador, NullLogger<ProcesadorOutbox>.Instance);
        Barrido = new BarridoTiempo(fabrica, evaluador, NullLogger<BarridoTiempo>.Instance);
    }

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
            FechaCreacion = DateTime.UtcNow
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

        var ahora = DateTime.UtcNow;

        foreach (var conversacion in await Db.Conversaciones.ToListAsync())
        {
            conversacion.FechaUltimaActividad = ahora;
            conversacion.FechaUltimoMensajeEntrante = ahora;
        }

        await Db.SaveChangesAsync();
    }
    /// <summary>Vacia la cola atendible, como haria una vuelta del ConsumidorOutbox.</summary>
    public async Task<int> ConsumirOutboxAsync()
    {
        var pendientes = await Eventos.ObtenerPendientesAsync(50, ProcesadorOutbox.TiposQueAtiende);

        foreach (var evento in pendientes)
        {
            await Procesador.ProcesarAsync(evento);
            await Eventos.MarcarProcesadoAsync(evento.EventoId);
        }

        return pendientes.Count;
    }


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

        conversacion.FechaUltimaActividad = DateTime.UtcNow;
        conversacion.FechaUltimoMensajeEntrante = DateTime.UtcNow;

        await Db.SaveChangesAsync();
    }
    public void Dispose()
    {
        Db.Dispose();

        if (Directory.Exists(CarpetaCv))
            Directory.Delete(CarpetaCv, recursive: true);
    }
}
