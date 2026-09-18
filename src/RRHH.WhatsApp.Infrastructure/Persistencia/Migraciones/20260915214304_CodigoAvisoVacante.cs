using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RRHH.WhatsApp.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class CodigoAvisoVacante : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CodigoAviso",
                table: "HC",
                type: "nvarchar(12)",
                maxLength: 12,
                nullable: true);

            // A6: toda vacante existente necesita su codigo para el enlace del aviso. Seis caracteres de
            // un alfabeto sin ambiguos (sin 0/O ni 1/I/L): el postulante lo puede tener que escribir a
            // mano, y "Postulo 0I1" es el tipo de codigo que nadie reconoce. Las vacantes nuevas reciben
            // el suyo del servicio, con el mismo alfabeto (T3.07).
            //
            // Se asigna a todas las filas de una vez y despues se anulan las colisiones, en vez de
            // recorrer fila por fila: en un servidor cargado un bucle por fila supera el tiempo de espera
            // de la migracion aunque haya pocas vacantes. Con 31^6 combinaciones la segunda vuelta casi
            // nunca hace falta.
            migrationBuilder.Sql("""
                DECLARE @alfabeto varchar(31) = '23456789ABCDEFGHJKMNPQRSTUVWXYZ';

                WHILE EXISTS (SELECT 1 FROM HC WHERE CodigoAviso IS NULL)
                BEGIN
                    UPDATE h
                    SET CodigoAviso = CONCAT(
                        SUBSTRING(@alfabeto, 1 + ABS(CHECKSUM(NEWID(), h.HcId, 1) % 31), 1),
                        SUBSTRING(@alfabeto, 1 + ABS(CHECKSUM(NEWID(), h.HcId, 2) % 31), 1),
                        SUBSTRING(@alfabeto, 1 + ABS(CHECKSUM(NEWID(), h.HcId, 3) % 31), 1),
                        SUBSTRING(@alfabeto, 1 + ABS(CHECKSUM(NEWID(), h.HcId, 4) % 31), 1),
                        SUBSTRING(@alfabeto, 1 + ABS(CHECKSUM(NEWID(), h.HcId, 5) % 31), 1),
                        SUBSTRING(@alfabeto, 1 + ABS(CHECKSUM(NEWID(), h.HcId, 6) % 31), 1))
                    FROM HC h
                    WHERE h.CodigoAviso IS NULL;

                    WITH repetidos AS (
                        SELECT HcId, ROW_NUMBER() OVER (PARTITION BY CodigoAviso ORDER BY HcId) AS orden
                        FROM HC
                        WHERE CodigoAviso IS NOT NULL)
                    UPDATE HC SET CodigoAviso = NULL
                    WHERE HcId IN (SELECT HcId FROM repetidos WHERE orden > 1);
                END
                """);

            migrationBuilder.CreateIndex(
                name: "IX_HC_CodigoAviso",
                table: "HC",
                column: "CodigoAviso",
                unique: true,
                filter: "[CodigoAviso] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HC_CodigoAviso",
                table: "HC");

            migrationBuilder.DropColumn(
                name: "CodigoAviso",
                table: "HC");
        }
    }
}
