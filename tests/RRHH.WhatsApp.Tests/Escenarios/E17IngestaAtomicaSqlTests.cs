using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Proveedores;
using RRHH.WhatsApp.Infrastructure.Servicios;
using RRHH.WhatsApp.Tests.Infraestructura;
using RRHH.WhatsApp.Tests.Proveedores;

namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// E17 (C6, COR-05): si la publicación del evento falla después de guardar el mensaje, no queda
/// nada a medias y la reentrega de Meta se procesa.
/// <para>
/// Antes quedaba el mensaje sin evento: Meta reentregaba, el duplicado se descartaba por el
/// <c>ProviderMessageId</c> y el evento no existía nunca. Nadie le respondía al postulante. Solo se
/// ve contra SQL Server: en memoria no hay transacción que deshacer.
/// </para>
/// </summary>
public class E17IngestaAtomicaSqlTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    private const string Telefono = "+51987654321";

    private static RecepcionWebhook Recepcion(RrhhDbContext db, IEventoSistemaService eventos) => new(
        new ProveedorSimulado(TimeProvider.System, NullLogger<ProveedorSimulado>.Instance),
        ServiciosDePrueba.Conversaciones(db, TimeProvider.System),
        new MensajeService(db, TimeProvider.System, NullLogger<MensajeService>.Instance),
        eventos,
        new UnidadTrabajoEf(db),
        NullLogger<RecepcionWebhook>.Instance);

    private static EventoSistemaService Eventos(RrhhDbContext db) =>
        new(db, TimeProvider.System, NullLogger<EventoSistemaService>.Instance);

    [FactConSqlServer]
    public async Task Si_publicar_el_evento_falla_no_queda_el_mensaje_y_la_reentrega_se_procesa()
    {
        await using (var db = sql.CrearContexto())
        {
            var recepcion = Recepcion(db, new EventosQueFallanAlPublicar(Eventos(db)));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                recepcion.ProcesarAsync(PayloadsDePrueba.MensajeDeTexto, new Dictionary<string, string>()));
        }

        await using (var db = sql.CrearContexto())
        {
            Assert.Equal(0, await db.Mensajes.CountAsync(m => m.Conversacion!.TelefonoE164 == Telefono));
            Assert.Equal(0, await db.EventosSistema.CountAsync());
        }

        // Meta reentrega el mismo payload —mismo wamid— porque recibió un 500.
        await using (var db = sql.CrearContexto())
        {
            var resultado = await Recepcion(db, Eventos(db))
                .ProcesarAsync(PayloadsDePrueba.MensajeDeTexto, new Dictionary<string, string>());

            Assert.Equal(1, resultado.MensajesNuevos);
            Assert.Equal(0, resultado.MensajesDuplicados);
        }

        await using (var db = sql.CrearContexto())
        {
            Assert.Equal(1, await db.Mensajes.CountAsync(m => m.Conversacion!.TelefonoE164 == Telefono));
            Assert.Equal(1, await db.EventosSistema.CountAsync(e => e.Tipo == TiposEvento.MensajeEntranteRecibido));
        }
    }
}
