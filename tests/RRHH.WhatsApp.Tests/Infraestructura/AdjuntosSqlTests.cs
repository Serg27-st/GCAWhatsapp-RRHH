using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Proveedores;
using RRHH.WhatsApp.Infrastructure.Servicios;
using RRHH.WhatsApp.Tests.Escenarios;
using RRHH.WhatsApp.Tests.Proveedores;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// T5.02 (ARQ-10) contra el esquema real de la migración <c>AdjuntosEntrantes</c>: el adjunto entra en
/// la misma transacción que su mensaje (V28) y se va con él.
/// </summary>
public class AdjuntosSqlTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    private static RecepcionWebhook Recepcion(RrhhDbContext db, IEventoSistemaService eventos) => new(
        new ProveedorSimulado(TimeProvider.System, NullLogger<ProveedorSimulado>.Instance),
        ServiciosDePrueba.Conversaciones(db, TimeProvider.System),
        new MensajeService(db, TimeProvider.System, NullLogger<MensajeService>.Instance),
        eventos,
        new UnidadTrabajoEf(db),
        NullLogger<RecepcionWebhook>.Instance);

    private static EventoSistemaService Eventos(RrhhDbContext db) =>
        new(db, TimeProvider.System, NullLogger<EventoSistemaService>.Instance);

    private static Task<int> AdjuntosDelDocumentoAsync(RrhhDbContext db) =>
        db.MensajesAdjuntos.CountAsync(a => a.ProveedorMedioId == "1037543291543636");

    /// <summary>
    /// Sin la transacción quedaría un adjunto de un mensaje que nunca se procesó, o peor: la reentrega
    /// descartaría el mensaje por duplicado y el archivo no se registraría nunca.
    /// </summary>
    [FactConSqlServer]
    public async Task Si_publicar_falla_no_queda_el_adjunto_y_la_reentrega_lo_registra()
    {
        await using (var db = sql.CrearContexto())
        {
            var recepcion = Recepcion(db, new EventosQueFallanAlPublicar(Eventos(db)));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                recepcion.ProcesarAsync(PayloadsDePrueba.Adjunto, new Dictionary<string, string>()));
        }

        await using (var db = sql.CrearContexto())
            Assert.Equal(0, await AdjuntosDelDocumentoAsync(db));

        await using (var db = sql.CrearContexto())
            await Recepcion(db, Eventos(db)).ProcesarAsync(PayloadsDePrueba.Adjunto, new Dictionary<string, string>());

        await using (var db = sql.CrearContexto())
        {
            var adjunto = await db.MensajesAdjuntos.AsNoTracking()
                .SingleAsync(a => a.ProveedorMedioId == "1037543291543636");

            Assert.Equal(EstadoAdjunto.Pendiente, adjunto.Estado);
            Assert.Equal("cv.pdf", adjunto.NombreArchivo);
            Assert.Equal("application/pdf", adjunto.MimeType);

            // Borrar el mensaje se lleva el adjunto: no queda un archivo colgado de nada (Regla 17).
            await db.Mensajes.Where(m => m.MensajeId == adjunto.MensajeId).ExecuteDeleteAsync();

            Assert.Equal(0, await AdjuntosDelDocumentoAsync(db));
        }
    }
}
