using Microsoft.Extensions.Logging.Abstractions;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Servicios;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// T5.05 (Regla 17 con el criterio de A5, V33): la consulta que elige qué adjuntos purgar cruza el
/// mensaje, su conversación y las postulaciones de la persona. En memoria eso siempre funciona; lo que
/// hay que probar es que EF lo traduzca a SQL en vez de traerse la tabla entera.
/// </summary>
public class PurgaAdjuntosSqlTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    private const int DiasRetencion = 365;

    private static MensajeService Servicio(RrhhDbContext db) =>
        new(db, TimeProvider.System, NullLogger<MensajeService>.Instance);

    /// <summary>Deja una conversación con un adjunto descargado y devuelve su id.</summary>
    private static async Task<long> ConAdjuntoAsync(
        RrhhDbContext db, DateTime actividad, Postulante? postulante = null)
    {
        var conversacion = new Conversacion
        {
            TelefonoE164 = $"+519{Random.Shared.Next(10000000, 99999999)}",
            Estado = EstadoConversacion.Activa,
            PostulanteId = postulante?.PostulanteId,
            FechaCreacion = actividad,
            FechaUltimaActividad = actividad
        };

        db.Conversaciones.Add(conversacion);
        await db.SaveChangesAsync();

        var mensaje = new Mensaje
        {
            ConversacionId = conversacion.ConversacionId,
            Direccion = DireccionMensaje.Entrante,
            Contenido = "[documento: cv.pdf]",
            EstadoEntrega = EstadoEntrega.Entregado,
            FechaEnvio = actividad
        };

        db.Mensajes.Add(mensaje);
        await db.SaveChangesAsync();

        var adjunto = new MensajeAdjunto
        {
            MensajeId = mensaje.MensajeId,
            TipoMedio = "document",
            ProveedorMedioId = Guid.NewGuid().ToString("N"),
            MimeType = "application/pdf",
            NombreArchivo = "cv.pdf",
            Ruta = "2025-09/archivo.pdf",
            Estado = EstadoAdjunto.Descargado,
            FechaRecepcion = actividad
        };

        db.MensajesAdjuntos.Add(adjunto);
        await db.SaveChangesAsync();

        return adjunto.AdjuntoId;
    }

    [FactConSqlServer]
    public async Task La_retencion_por_ultima_actividad_se_resuelve_en_la_base()
    {
        await using var db = sql.CrearContexto();

        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var cuenta = new Cuenta { Nombre = $"Cuenta {sufijo}", Activo = true };
        db.Cuentas.Add(cuenta);
        await db.SaveChangesAsync();

        var vacante = new Hc
        {
            CuentaId = cuenta.CuentaId,
            Titulo = "Operario",
            Estado = EstadoHc.Abierta,
            CodigoAviso = sufijo[..6].ToUpperInvariant(),
            FechaCreacion = DateTime.UtcNow
        };

        db.Hcs.Add(vacante);

        var enProceso = new Postulante { Dni = $"1{Random.Shared.Next(1000000, 9999999)}", FechaRegistro = DateTime.UtcNow };
        var descartado = new Postulante { Dni = $"2{Random.Shared.Next(1000000, 9999999)}", FechaRegistro = DateTime.UtcNow };

        db.Postulantes.AddRange(enProceso, descartado);
        await db.SaveChangesAsync();

        var viejo = DateTime.UtcNow.AddDays(-DiasRetencion - 10);

        db.Postulaciones.AddRange(
            // Sigue en proceso: su archivo no se toca, por viejo que sea el mensaje (A5).
            new Postulacion
            {
                PostulanteId = enProceso.PostulanteId,
                HcId = vacante.HcId,
                CuentaId = cuenta.CuentaId,
                EtapaKanbanId = 1,
                Estado = EstadoPostulacion.EnProceso,
                FechaCreacion = viejo,
                FechaUltimaActividad = viejo
            },
            // Descartado hace poco: la actividad reciente de la postulacion sostiene el plazo.
            new Postulacion
            {
                PostulanteId = descartado.PostulanteId,
                HcId = vacante.HcId,
                CuentaId = cuenta.CuentaId,
                EtapaKanbanId = 1,
                Estado = EstadoPostulacion.Descartado,
                FechaCreacion = viejo,
                FechaUltimaActividad = DateTime.UtcNow.AddDays(-5)
            });

        await db.SaveChangesAsync();

        var sinPostulante = await ConAdjuntoAsync(db, viejo);
        var conProcesoVivo = await ConAdjuntoAsync(db, viejo, enProceso);
        var conActividadReciente = await ConAdjuntoAsync(db, viejo, descartado);
        var reciente = await ConAdjuntoAsync(db, DateTime.UtcNow.AddDays(-3));

        var porPurgar = await Servicio(db).ListarAdjuntosPorPurgarAsync(DiasRetencion, maximo: 50);
        var ids = porPurgar.Select(a => a.AdjuntoId).ToList();

        Assert.Contains(sinPostulante, ids);
        Assert.DoesNotContain(conProcesoVivo, ids);
        Assert.DoesNotContain(conActividadReciente, ids);
        Assert.DoesNotContain(reciente, ids);
    }
}
