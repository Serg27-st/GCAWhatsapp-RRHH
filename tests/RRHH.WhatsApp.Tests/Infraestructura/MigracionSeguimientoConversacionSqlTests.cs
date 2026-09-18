using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// T2.04 (V30): la migración <c>SeguimientoConversacion</c> reparte los hilos que antes vivían todos en
/// PendienteClasificar. Una migración de datos mal escrita no la detecta ninguna prueba en memoria, y
/// equivocarse acá deja conversaciones en la bandeja equivocada en producción.
/// </summary>
public class MigracionSeguimientoConversacionSqlTests
{
    private const string MigracionAnterior = "LimpiezaEventosHuerfanos";

    [FactConSqlServer]
    public async Task Solo_lo_que_la_Regla_19_derivo_sigue_sin_clasificar_y_el_resto_vuelve_al_menu_del_bot()
    {
        var cadena = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(FactConSqlServerAttribute.Variable))
        {
            InitialCatalog = $"RRHH_Pruebas_{Guid.NewGuid():N}"
        }.ConnectionString;

        await using var db = new RrhhDbContext(new DbContextOptionsBuilder<RrhhDbContext>()
            .UseSqlServer(cadena, o => o.CommandTimeout(SqlServerFixture.TiempoEsperaComandosSegundos))
            .Options);

        try
        {
            await db.GetService<IMigrator>().MigrateAsync(MigracionAnterior);

            // Tres hilos con el esquema viejo: uno que el bot atendía, uno que la Regla 19 derivó y uno escalado.
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO Conversaciones (TelefonoE164, Estado, FechaUltimaActividad, FechaCreacion)
                VALUES ('+51900000001', 3, '2026-09-01T10:00:00', '2026-09-01T10:00:00'),
                       ('+51900000002', 3, '2026-09-02T11:00:00', '2026-09-02T10:00:00'),
                       ('+51900000003', 2, '2026-09-03T12:00:00', '2026-09-03T10:00:00');

                INSERT INTO Auditoria (EntidadTipo, EntidadId, Accion, Fecha, Detalle)
                SELECT 'Conversacion', CAST(ConversacionId AS nvarchar(50)), 'DerivadaABandejaGeneral', '2026-09-02T11:00:00', NULL
                FROM Conversaciones WHERE TelefonoE164 = '+51900000002';

                INSERT INTO Auditoria (EntidadTipo, EntidadId, Accion, Fecha, Detalle)
                SELECT 'Conversacion', CAST(ConversacionId AS nvarchar(50)), 'Escalamiento', '2026-09-03T12:30:00', NULL
                FROM Conversaciones WHERE TelefonoE164 = '+51900000003';
                """);

            await db.GetService<IMigrator>().MigrateAsync();

            var hilos = await db.Conversaciones.AsNoTracking().OrderBy(c => c.TelefonoE164).ToListAsync();

            Assert.Equal(Domain.Enums.EstadoConversacion.EnMenuBot, hilos[0].Estado);
            Assert.Null(hilos[0].FechaPendienteDesde);

            Assert.Equal(Domain.Enums.EstadoConversacion.PendienteClasificar, hilos[1].Estado);
            Assert.Equal(new DateTime(2026, 9, 2, 11, 0, 0), hilos[1].FechaPendienteDesde);

            Assert.Equal(new DateTime(2026, 9, 3, 12, 30, 0), hilos[2].FechaEscalamiento);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await db.Database.EnsureDeletedAsync();
        }
    }
}
