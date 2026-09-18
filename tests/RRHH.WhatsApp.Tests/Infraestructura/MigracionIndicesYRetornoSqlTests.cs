using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// T2.09 (B9): si hay dos respuestas del JobForms para la misma invitación, la migración
/// <c>IndicesYRetorno</c> se detiene con un mensaje que dice cuáles, en vez de borrar datos de un
/// postulante por su cuenta o fallar con un error de índice que nadie entiende.
/// </summary>
public class MigracionIndicesYRetornoSqlTests
{
    private const string MigracionAnterior = "VencimientoTransferencias";

    [FactConSqlServer]
    public async Task Con_respuestas_duplicadas_la_migracion_se_detiene_y_dice_cuales()
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

            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO Cuentas (Nombre, Activo) VALUES ('Cuenta duplicados', 1);
                DECLARE @cuenta int = SCOPE_IDENTITY();

                INSERT INTO HC (CuentaId, Titulo, Estado, FechaCreacion, CodigoAviso) VALUES (@cuenta, 'Operario', 1, '2026-09-01', 'ABCDEF');
                DECLARE @hc int = SCOPE_IDENTITY();

                INSERT INTO Postulantes (Dni, FechaRegistro) VALUES ('45678912', '2026-09-01');
                DECLARE @postulante int = SCOPE_IDENTITY();

                INSERT INTO Conversaciones (TelefonoE164, Estado, FechaUltimaActividad, FechaCreacion, IntentosMenuFallidos)
                VALUES ('+51900000009', 1, '2026-09-01', '2026-09-01', 0);
                DECLARE @conversacion int = SCOPE_IDENTITY();

                INSERT INTO JobFormsInvitaciones (ConversacionId, HcId, Token, FechaEnvioLink, Completado, RecordatorioEnviado, AvisoAnalistaEnviado)
                VALUES (@conversacion, @hc, NEWID(), '2026-09-01', 1, 0, 0);
                DECLARE @invitacion int = SCOPE_IDENTITY();

                INSERT INTO JobFormsRespuestas (InvitacionId, PostulanteId, HcId, DatosJson, ConsentimientoAceptado, FechaEnvio)
                VALUES (@invitacion, @postulante, @hc, '{{}}', 1, '2026-09-01'),
                       (@invitacion, @postulante, @hc, '{{}}', 1, '2026-09-01');
                """);

            var error = await Assert.ThrowsAsync<SqlException>(() => db.GetService<IMigrator>().MigrateAsync());

            Assert.Contains("respuestas duplicadas", error.Message);

            // Nada de la migración quedó aplicado: la respuesta duplicada sigue ahí para que alguien decida.
            Assert.Equal(2, await db.JobFormsRespuestas.CountAsync());
            Assert.DoesNotContain("IndicesYRetorno", await db.Database.GetAppliedMigrationsAsync().ContinueWith(t => string.Join(",", t.Result)));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await db.Database.EnsureDeletedAsync();
        }
    }
}
