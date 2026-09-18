using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Application.Reglas;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Proveedores;
using RRHH.WhatsApp.Infrastructure.Servicios;
using RRHH.WhatsApp.Tests.Casos;
using RRHH.WhatsApp.Tests.Infraestructura;
using RRHH.WhatsApp.Tests.Proveedores;

namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// E16 (C5, COR-04): reprocesar un evento no le manda nada dos veces al postulante.
/// <para>
/// Es el patrón que le costó la línea a la empresa. Antes, si algo fallaba después de enviar —la
/// auditoría, la marca de procesado, la base—, el evento volvía a Pendiente con la mitad aplicada y
/// el siguiente ciclo volvía a mandar el menú. Ahora las reglas solo encolan con clave única (V29) y
/// el evento se confirma o se deshace entero (V28).
/// </para>
/// </summary>
public class E16SinDuplicadosTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task Procesar_dos_veces_el_mismo_evento_deja_un_mensaje_y_un_envio()
    {
        using var entorno = new EntornoDeReglas();

        await entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);

        var evento = Assert.Single(await entorno.Eventos.ObtenerPendientesAsync(10, ProcesadorOutbox.TiposQueAtiende));

        // El Worker murió después de procesar y antes de marcarlo: lo vuelve a tomar.
        await entorno.Procesador.ProcesarAsync(evento);
        await entorno.Procesador.ProcesarAsync(evento);
        await entorno.DespacharAsync();

        Assert.Equal(1, await entorno.Db.Mensajes.CountAsync(m => m.Direccion == DireccionMensaje.Saliente));
        Assert.Single(entorno.Proveedor.Enviados);
    }

    [Fact]
    public async Task Una_falla_despues_de_encolar_y_el_reintento_dejan_un_solo_envio()
    {
        var auditoria = new AuditoriaQueFallaUnaVez();
        using var entorno = new EntornoDeReglas(real => auditoria.Envolver(real));

        await entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);

        // Elegir la empresa: la Regla 9 encola el enlace del formulario y después audita. La auditoría
        // falla con el enlace ya encolado.
        await Assert.ThrowsAsync<InvalidOperationException>(() => entorno.ConsumirOutboxAsync(despachar: false));

        Assert.Single(await entorno.Eventos.ObtenerPendientesAsync(10, ProcesadorOutbox.TiposQueAtiende));

        // El consumidor lo reintenta en la vuelta siguiente, y el despachador manda lo encolado.
        await entorno.ConsumirOutboxAsync(despachar: true);

        Assert.Empty(await entorno.Eventos.ObtenerPendientesAsync(10, ProcesadorOutbox.TiposQueAtiende));
        Assert.Equal(1, await entorno.Db.Mensajes.CountAsync(m => m.Direccion == DireccionMensaje.Saliente));
        Assert.Single(entorno.Proveedor.Enviados);
    }

    [FactConSqlServer]
    public async Task Contra_SQL_Server_el_intento_fallido_no_deja_filas_y_el_reintento_envia_una_vez()
    {
        // El reloj a un minuto del timestamp del payload: la ventana de 24h está abierta.
        var reloj = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1755600100).AddMinutes(1));
        var proveedor = new ProveedorFalso();

        int cuentaId;

        await using (var db = sql.CrearContexto())
            cuentaId = await SembrarCuentaConVacanteAsync(db, reloj);

        // El botón del payload apunta a la cuenta que generó la base, no a la 7 del entorno en memoria.
        var eligeLaEmpresa = PayloadsDePrueba.RespuestaDeBoton.Replace("cuenta_7", $"cuenta_{cuentaId}");

        await using (var db = sql.CrearContexto())
        {
            var circuito = Circuito(db, reloj, proveedor, new AuditoriaQueFallaUnaVez());
            await circuito.Recepcion.ProcesarAsync(eligeLaEmpresa, new Dictionary<string, string>());

            await Assert.ThrowsAsync<InvalidOperationException>(() => circuito.ConsumirAsync());
        }

        await using (var db = sql.CrearContexto())
        {
            // Nada del primer intento: ni la asignación, ni la invitación, ni el enlace encolado, ni la
            // auditoría. El evento sigue pendiente.
            Assert.Equal(0, await db.Mensajes.CountAsync(m => m.Direccion == DireccionMensaje.Saliente));
            Assert.Equal(0, await db.JobFormsInvitaciones.CountAsync());
            Assert.Null((await db.Conversaciones.AsNoTracking().SingleAsync()).AnalistaAtendiendoId);
            Assert.Equal(0, await db.Auditorias.CountAsync(a => a.EntidadTipo == nameof(Conversacion)));
            Assert.Equal(1, await db.EventosSistema.CountAsync(e => e.Estado == EstadoEvento.Pendiente
                && e.Tipo == TiposEvento.MensajeEntranteRecibido));
        }

        await using (var db = sql.CrearContexto())
        {
            var circuito = Circuito(db, reloj, proveedor, auditoria: null);

            await circuito.ConsumirAsync();
            await circuito.Despacho.ProcesarAsync(10, TimeSpan.FromMinutes(2));
        }

        await using (var db = sql.CrearContexto())
        {
            Assert.Equal(1, await db.Mensajes.CountAsync(m => m.Direccion == DireccionMensaje.Saliente));
            Assert.Equal(1, proveedor.Llamadas);
        }
    }

    private static async Task<int> SembrarCuentaConVacanteAsync(RrhhDbContext db, TimeProvider reloj)
    {
        var cuenta = new Cuenta { Nombre = "Alicorp", Activo = true };
        var titular = new Analista { Nombre = "Ana Torres", Email = "ana.e16@gca.pe", Activo = true };

        db.Cuentas.Add(cuenta);
        db.Analistas.Add(titular);
        await db.SaveChangesAsync();

        db.AnalistaCuentas.Add(new AnalistaCuenta { AnalistaId = titular.AnalistaId, CuentaId = cuenta.CuentaId, EsBackup = false });

        db.Hcs.Add(new Hc
        {
            CuentaId = cuenta.CuentaId,
            Titulo = "Operario",
            UrlJobForms = "https://forms.gle/operario",
            Estado = EstadoHc.Abierta,
            FechaCreacion = reloj.GetUtcNow().UtcDateTime
        });

        await db.SaveChangesAsync();

        return cuenta.CuentaId;
    }

    /// <summary>
    /// El mismo grafo que <see cref="EntornoDeReglas"/>, sobre SQL Server. Aparte porque el entorno
    /// siembra con ids fijos, y SQL Server no acepta valores explícitos en columnas identidad.
    /// </summary>
    private static CircuitoSql Circuito(RrhhDbContext db, TimeProvider reloj, IWhatsAppProvider proveedor, AuditoriaQueFallaUnaVez? auditoria)
    {
        var configuracion = new ConfiguracionReglasService(db, new MemoryCache(new MemoryCacheOptions()), reloj);
        var plantillas = new PlantillaService(db, reloj, NullLogger<PlantillaService>.Instance);
        var conversaciones = ServiciosDePrueba.Conversaciones(db, reloj);
        var mensajes = new MensajeService(db, reloj, NullLogger<MensajeService>.Instance);
        var eventos = new EventoSistemaService(db, reloj, NullLogger<EventoSistemaService>.Instance);
        var postulaciones = new PostulacionService(db, reloj, NullLogger<PostulacionService>.Instance);
        var invitaciones = new JobFormsInvitacionService(db, reloj, NullLogger<JobFormsInvitacionService>.Instance);
        var unidad = new UnidadTrabajoEf(db);

        IAuditoriaService registro = new AuditoriaService(db, reloj);
        registro = auditoria?.Envolver(registro) ?? registro;

        var fabrica = new FabricaContextoRegla(
            db, configuracion, new HorarioAtencionService(db, reloj, NullLogger<HorarioAtencionService>.Instance),
            new AusenciaService(db, reloj), new CuentaService(db, new AlertaOperativaService(db, reloj), reloj), reloj);

        var ejecutor = new EjecutorAcciones(
            conversaciones, mensajes, plantillas, invitaciones, postulaciones, eventos, registro,
            new AlertaOperativaService(db, reloj),
            new CuentaService(db, new AlertaOperativaService(db, reloj), reloj), ServiciosDePrueba.Analistas(db, reloj), NullLogger<EjecutorAcciones>.Instance);

        var evaluador = new EvaluadorReglas(
            new MotorReglas(EntornoDeReglas.ReglasEnOrden(), NullLogger<MotorReglas>.Instance),
            ejecutor, NullLogger<EvaluadorReglas>.Instance);

        return new CircuitoSql(
            // Recibe con el simulado, que interpreta el payload de Meta; envía con el falso, que cuenta.
            new RecepcionWebhook(new ProveedorSimulado(reloj, NullLogger<ProveedorSimulado>.Instance),
                conversaciones, mensajes, eventos, unidad, NullLogger<RecepcionWebhook>.Instance),
            new ProcesadorOutbox(
                fabrica, evaluador, postulaciones, configuracion, NullLogger<ProcesadorOutbox>.Instance),
            new DespachoEnvios(mensajes, conversaciones, plantillas, proveedor, new ValidadorEnvio(conversaciones, plantillas),
                configuracion, reloj, NullLogger<DespachoEnvios>.Instance),
            eventos,
            unidad);
    }

    private sealed record CircuitoSql(
        RecepcionWebhook Recepcion,
        ProcesadorOutbox Procesador,
        DespachoEnvios Despacho,
        IEventoSistemaService Eventos,
        IUnidadTrabajo Unidad)
    {
        /// <summary>Lo mismo que hace <c>ConsumidorOutbox.IntentarAsync</c> por evento.</summary>
        public async Task ConsumirAsync()
        {
            foreach (var evento in await Eventos.ObtenerPendientesAsync(10, ProcesadorOutbox.TiposQueAtiende))
            {
                await Unidad.EjecutarAsync(async c =>
                {
                    await Procesador.ProcesarAsync(evento, c);
                    await Eventos.MarcarProcesadoAsync(evento.EventoId, c);
                });
            }
        }
    }

    /// <summary>Falla la primera vez que se audita, que en el flujo del menú es después de encolarlo.</summary>
    private sealed class AuditoriaQueFallaUnaVez
    {
        private bool _fallo;

        public IAuditoriaService Envolver(IAuditoriaService real) => new Decorador(real, this);

        private sealed class Decorador(IAuditoriaService real, AuditoriaQueFallaUnaVez estado) : IAuditoriaService
        {
            public Task RegistrarAsync(string entidadTipo, string entidadId, int? analistaId,
                string accion, string? detalle, CancellationToken ct = default)
            {
                if (!estado._fallo)
                {
                    estado._fallo = true;
                    throw new InvalidOperationException("Se cayó la base al auditar.");
                }

                return real.RegistrarAsync(entidadTipo, entidadId, analistaId, accion, detalle, ct);
            }

            public Task<IReadOnlyList<Auditoria>> ListarPorEntidadAsync(
                string entidadTipo, string entidadId, CancellationToken ct = default) =>
                real.ListarPorEntidadAsync(entidadTipo, entidadId, ct);

            public Task AnonimizarDetallesAsync(
                IReadOnlyCollection<int> conversacionIds, int postulanteId, CancellationToken ct = default) =>
                real.AnonimizarDetallesAsync(conversacionIds, postulanteId, ct);
        }
    }
}
