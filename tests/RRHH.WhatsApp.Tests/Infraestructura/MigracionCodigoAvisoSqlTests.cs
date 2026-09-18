using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// T2.07 (A6, FUN-02): la migración <c>CodigoAvisoVacante</c> le da un código único a cada vacante
/// existente, con un alfabeto que un postulante pueda transcribir sin confundirse.
/// </summary>
public class MigracionCodigoAvisoSqlTests
{
    private const string MigracionAnterior = "DesenlaceYCierre";

    [FactConSqlServer]
    public async Task Todas_las_vacantes_existentes_quedan_con_un_codigo_unico_sin_caracteres_ambiguos()
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

            // Cuarenta vacantes: con seis caracteres de 31 símbolos una colisión es rarísima, pero el
            // bucle tiene que terminar igual y dejar todas con código.
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO Cuentas (Nombre, Activo) VALUES ('Cuenta migracion', 1);

                DECLARE @cuenta int = SCOPE_IDENTITY();
                DECLARE @n int = 0;

                WHILE @n < 40
                BEGIN
                    INSERT INTO HC (CuentaId, Titulo, Estado, FechaCreacion)
                    VALUES (@cuenta, CONCAT('Vacante ', @n), 1, '2026-09-01T10:00:00');
                    SET @n = @n + 1;
                END
                """);

            await db.GetService<IMigrator>().MigrateAsync();

            var codigos = await db.Hcs.AsNoTracking().Select(h => h.CodigoAviso).ToListAsync();

            Assert.Equal(40, codigos.Count);
            Assert.All(codigos, c => Assert.Matches("^[23456789ABCDEFGHJKMNPQRSTUVWXYZ]{6}$", c));
            Assert.Equal(40, codigos.Distinct().Count());
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await db.Database.EnsureDeletedAsync();
        }
    }
}
