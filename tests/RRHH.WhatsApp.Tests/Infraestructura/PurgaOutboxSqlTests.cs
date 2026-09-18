using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Servicios;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// T5.06 (ARQ-13): la outbox no puede crecer para siempre. Se prueba contra SQL Server porque el
/// borrado por lotes es una sentencia real —<c>ExecuteDelete</c>—, que en memoria no existe.
/// <para>
/// Lo que importa es qué se borra y qué no: un evento pendiente o fallido tiene que sobrevivir, por
/// viejo que sea, porque todavía hay algo que hacer con él.
/// </para>
/// </summary>
public class PurgaOutboxSqlTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    private const int Retencion = 30;

    private static EventoSistemaService Servicio(RrhhDbContext db) =>
        new(db, TimeProvider.System, NullLogger<EventoSistemaService>.Instance);

    private static EventoSistema Evento(EstadoEvento estado, DateTime fecha) => new()
    {
        Tipo = "MensajeEntranteRecibido",
        Payload = "{}",
        Estado = estado,
        CorrelationId = Guid.NewGuid(),
        FechaCreacion = fecha,
        FechaProcesado = estado == EstadoEvento.Procesado ? fecha : null
    };

    [FactConSqlServer]
    public async Task Los_procesados_viejos_se_borran_en_lotes_y_el_resto_queda()
    {
        await using var db = sql.CrearContexto();

        var viejo = DateTime.UtcNow.AddDays(-Retencion - 1);
        var reciente = DateTime.UtcNow.AddDays(-1);

        for (var i = 0; i < 5; i++)
            db.EventosSistema.Add(Evento(EstadoEvento.Procesado, viejo));

        db.EventosSistema.Add(Evento(EstadoEvento.Procesado, reciente));

        // Pendiente y fallido no se tocan: uno espera consumidor y el otro espera a una persona.
        db.EventosSistema.Add(Evento(EstadoEvento.Pendiente, viejo));
        db.EventosSistema.Add(Evento(EstadoEvento.Fallido, viejo));

        await db.SaveChangesAsync();

        // Lote chico a proposito: la purga tiene que dar varias vueltas, no una sentencia gigante que
        // bloquee la tabla mientras el Worker sigue publicando.
        Assert.Equal(5, await Servicio(db).PurgarProcesadosAsync(Retencion, tamanoLote: 2));

        var quedaron = await db.EventosSistema.AsNoTracking().ToListAsync();

        Assert.Equal(3, quedaron.Count);
        Assert.Contains(quedaron, e => e.Estado == EstadoEvento.Procesado && e.FechaCreacion > viejo);
        Assert.Contains(quedaron, e => e.Estado == EstadoEvento.Pendiente);
        Assert.Contains(quedaron, e => e.Estado == EstadoEvento.Fallido);

        // La segunda vuelta no encuentra nada: no borra de mas ni queda girando.
        Assert.Equal(0, await Servicio(db).PurgarProcesadosAsync(Retencion, tamanoLote: 2));
    }
}
