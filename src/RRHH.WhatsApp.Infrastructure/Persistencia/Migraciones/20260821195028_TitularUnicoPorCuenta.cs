using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RRHH.WhatsApp.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class TitularUnicoPorCuenta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AnalistaCuenta_TitularUnicoPorCuenta",
                table: "AnalistaCuenta",
                column: "CuentaId",
                unique: true,
                filter: "[EsBackup] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AnalistaCuenta_TitularUnicoPorCuenta",
                table: "AnalistaCuenta");
        }
    }
}
