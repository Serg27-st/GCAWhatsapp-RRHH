using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Servicios;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// T1.06–T1.07 contra SQL Server: la clave de idempotencia la garantiza el índice único filtrado de
/// la migración <c>ColaDeEnvios</c>, no la comprobación previa del servicio. En memoria no hay índices.
/// </summary>
public class ColaDeEnviosSqlTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    [FactConSqlServer]
    public async Task Dos_filas_con_la_misma_clave_las_rechaza_la_base()
    {
        await using var db = sql.CrearContexto();

        var conversacion = await ServiciosDePrueba.Conversaciones(db, TimeProvider.System)
            .ObtenerOCrearAsync("+51977000111");

        Mensaje Fila() => new()
        {
            ConversacionId = conversacion.ConversacionId,
            Direccion = DireccionMensaje.Saliente,
            Contenido = "Hola",
            EstadoEntrega = EstadoEntrega.EnCola,
            ClaveIdempotencia = "evt:sql:0",
            FechaEnvio = DateTime.UtcNow
        };

        db.Mensajes.Add(Fila());
        await db.SaveChangesAsync();

        db.Mensajes.Add(Fila());

        // La comprobación previa del servicio no ve una carrera entre dos procesos; el índice sí.
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [FactConSqlServer]
    public async Task Varios_salientes_sin_clave_conviven()
    {
        // El índice se filtra: los entrantes y los salientes históricos no tienen clave.
        await using var db = sql.CrearContexto();

        var conversacion = await ServiciosDePrueba.Conversaciones(db, TimeProvider.System)
            .ObtenerOCrearAsync("+51977000222");

        var mensajes = new MensajeService(db, TimeProvider.System, NullLogger<MensajeService>.Instance);

        await mensajes.RegistrarSalienteAsync(conversacion.ConversacionId, "uno", null, null, null, Guid.NewGuid());
        await mensajes.RegistrarSalienteAsync(conversacion.ConversacionId, "dos", null, null, null, Guid.NewGuid());

        Assert.Equal(2, await db.Mensajes.CountAsync(m => m.ConversacionId == conversacion.ConversacionId));
    }

    [FactConSqlServer]
    public async Task El_servicio_traduce_la_clave_repetida_a_nulo()
    {
        await using var db = sql.CrearContexto();

        var conversacion = await ServiciosDePrueba.Conversaciones(db, TimeProvider.System)
            .ObtenerOCrearAsync("+51977000333");

        var mensajes = new MensajeService(db, TimeProvider.System, NullLogger<MensajeService>.Instance);
        var saliente = new SalienteEncolado(TipoSaliente.Texto, "Hola");

        Assert.NotNull(await mensajes.EncolarSalienteAsync(conversacion.ConversacionId, saliente, "evt:sql:1", null, Guid.NewGuid()));
        Assert.Null(await mensajes.EncolarSalienteAsync(conversacion.ConversacionId, saliente, "evt:sql:1", null, Guid.NewGuid()));
    }
}
