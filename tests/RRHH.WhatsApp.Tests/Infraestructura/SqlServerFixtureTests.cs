using Microsoft.EntityFrameworkCore;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// La base temporal de <see cref="SqlServerFixture"/> queda con el esquema y la semilla de producción.
/// Si esto falla, fallan todas las pruebas SQL por la misma causa: conviene verlo aislado.
/// </summary>
public class SqlServerFixtureTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    [FactConSqlServer]
    public async Task La_base_temporal_tiene_todas_las_migraciones_y_la_semilla()
    {
        await using var db = sql.CrearContexto();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        // Semilla de la migración inicial: las 6 plantillas, todas inactivas (V8).
        var plantillas = await db.Plantillas.AsNoTracking().ToListAsync();
        Assert.Equal(6, plantillas.Count);
        Assert.All(plantillas, p => Assert.False(p.Activa));
    }

    [FactConSqlServer]
    public async Task La_base_temporal_no_es_la_que_indica_la_variable()
    {
        await using var db = sql.CrearContexto();

        Assert.StartsWith("RRHH_Pruebas_", db.Database.GetDbConnection().Database);
    }
}
