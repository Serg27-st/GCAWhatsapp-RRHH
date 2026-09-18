using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Almacenamiento;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Servicios;
using RRHH.WhatsApp.Tests.Escenarios;
using RRHH.WhatsApp.Tests.Infraestructura;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// T1.04 (C6, COR-05): la recepción del formulario se confirma o se deshace entera.
/// <para>
/// Antes, si fallaba la publicación del evento, la invitación ya estaba marcada completada y la
/// respuesta guardada. El reintento del Apps Script recibía <c>yaRecibido</c> (V27) y nadie le
/// confirmaba ni le asignaba analista al postulante. Solo se ve contra SQL Server.
/// </para>
/// </summary>
public class RecepcionJobFormsSqlTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    private static RecepcionJobForms Recepcion(RrhhDbContext db, IEventoSistemaService eventos)
    {
        var almacenamiento = new AlmacenamientoCvLocal(
            Options.Create(new OpcionesCv { Carpeta = Path.Combine(Path.GetTempPath(), $"cv-sql-{Guid.NewGuid():N}") }),
            new EscanerDesactivado(NullLogger<EscanerDesactivado>.Instance),
            TimeProvider.System,
            NullLogger<AlmacenamientoCvLocal>.Instance);

        var configuracion = new ConfiguracionReglasService(db, new MemoryCache(new MemoryCacheOptions()), TimeProvider.System);

        return new RecepcionJobForms(
            new JobFormsInvitacionService(db, TimeProvider.System, NullLogger<JobFormsInvitacionService>.Instance),
            new JobFormsService(db, almacenamiento, configuracion, TimeProvider.System, NullLogger<JobFormsService>.Instance),
            ServiciosDePrueba.Postulantes(db, almacenamiento, AdjuntosDePrueba()),
            new PostulacionService(db, TimeProvider.System, NullLogger<PostulacionService>.Instance),
            ServiciosDePrueba.Conversaciones(db, TimeProvider.System),
            eventos,
            new UnidadTrabajoEf(db),
            NullLogger<RecepcionJobForms>.Instance);
    }

    private static EventoSistemaService Eventos(RrhhDbContext db) =>
        new(db, TimeProvider.System, NullLogger<EventoSistemaService>.Instance);

    /// <summary>Esta prueba no usa adjuntos; el servicio los pide porque la anonimizacion los alcanza.</summary>
    private static AlmacenamientoAdjuntosLocal AdjuntosDePrueba() =>
        new(Options.Create(new OpcionesAdjuntos()),
            Options.Create(new OpcionesCv { Carpeta = Path.Combine(Path.GetTempPath(), $"cv-sql-{Guid.NewGuid():N}") }),
            new EscanerDesactivado(NullLogger<EscanerDesactivado>.Instance),
            TimeProvider.System,
            NullLogger<AlmacenamientoAdjuntosLocal>.Instance);

    private async Task<JobFormsInvitacion> InvitacionAsync()
    {
        await using var db = sql.CrearContexto();

        var cuenta = new Cuenta { Nombre = $"Cuenta {Guid.NewGuid():N}"[..30], Activo = true };
        db.Cuentas.Add(cuenta);
        await db.SaveChangesAsync();

        var hc = new Hc
        {
            CuentaId = cuenta.CuentaId,
            Titulo = "Operario",
            UrlJobForms = "https://forms.gle/operario",
            Estado = EstadoHc.Abierta,
            FechaCreacion = DateTime.UtcNow
        };
        db.Hcs.Add(hc);
        await db.SaveChangesAsync();

        var conversacion = await ServiciosDePrueba.Conversaciones(db, TimeProvider.System)
            .ObtenerOCrearAsync("+51955111222");

        return await new JobFormsInvitacionService(db, TimeProvider.System, NullLogger<JobFormsInvitacionService>.Instance)
            .CrearInvitacionAsync(conversacion.ConversacionId, hc.HcId);
    }

    private static EnvioJobForms Envio(Guid token) => new(
        token,
        new DatosPostulanteFormulario("45678912", "Maria Quispe", "+51955111222", null),
        "{}",
        null,
        ConsentimientoAceptado: true);

    [FactConSqlServer]
    public async Task Si_publicar_falla_la_invitacion_sigue_abierta_y_el_reintento_se_procesa_completo()
    {
        var invitacion = await InvitacionAsync();

        await using (var db = sql.CrearContexto())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Recepcion(db, new EventosQueFallanAlPublicar(Eventos(db))).ProcesarAsync(Envio(invitacion.Token)));
        }

        await using (var db = sql.CrearContexto())
        {
            var despuesDelFallo = await db.JobFormsInvitaciones.AsNoTracking()
                .FirstAsync(i => i.InvitacionId == invitacion.InvitacionId);

            Assert.False(despuesDelFallo.Completado);
            Assert.False(await db.JobFormsRespuestas.AnyAsync(r => r.InvitacionId == invitacion.InvitacionId));
            Assert.False(await db.Postulantes.AnyAsync(p => p.Dni == "45678912"));
        }

        // El Apps Script reintenta porque recibió un 500.
        await using (var db = sql.CrearContexto())
        {
            var resultado = await Recepcion(db, Eventos(db)).ProcesarAsync(Envio(invitacion.Token));

            Assert.False(resultado.YaRecibido);
        }

        await using (var db = sql.CrearContexto())
        {
            Assert.True((await db.JobFormsInvitaciones.AsNoTracking()
                .FirstAsync(i => i.InvitacionId == invitacion.InvitacionId)).Completado);
            Assert.Equal(1, await db.EventosSistema.CountAsync(e => e.Tipo == TiposEvento.JobFormsCompletado));
        }
    }
}
