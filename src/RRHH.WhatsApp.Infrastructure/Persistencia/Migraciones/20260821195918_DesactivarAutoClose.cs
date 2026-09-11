using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RRHH.WhatsApp.Infrastructure.Persistencia.Migraciones
{
    /// <summary>
    /// SQL Server Express crea las bases con AUTO_CLOSE activado. Con esa opcion, la base se apaga
    /// al cerrarse la ultima conexion y cada reconexion tiene que volver a levantarla: bajo el pool
    /// de conexiones de la Api y el Worker, varias llegan a la vez, se encolan y expiran con un
    /// timeout previo al inicio de sesion. El sintoma es que el sistema "a veces no responde".
    /// <para>
    /// No cambia el esquema, pero va como migracion a proposito: es la unica forma de que todo
    /// ambiente que se despliegue arranque con la opcion correcta sin depender de que alguien se
    /// acuerde de ejecutarlo a mano.
    /// </para>
    /// </summary>
    public partial class DesactivarAutoClose : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DB_NAME() en vez del nombre fijo: los ambientes usan bases con nombres distintos.
            migrationBuilder.Sql(
                "DECLARE @sql nvarchar(max) = " +
                "N'ALTER DATABASE ' + QUOTENAME(DB_NAME()) + N' SET AUTO_CLOSE OFF WITH NO_WAIT;'; " +
                "EXEC sp_executesql @sql;",
                suppressTransaction: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A proposito no se revierte: volver a AUTO_CLOSE ON reintroduce la falla, y ninguna
            // otra parte del sistema depende de que este activado.
        }
    }
}
