using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RRHH.WhatsApp.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class ColaDeEnvios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClaveIdempotencia",
                table: "Mensajes",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaTomaEnvio",
                table: "Mensajes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OpcionesJson",
                table: "Mensajes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TipoSaliente",
                table: "Mensajes",
                type: "int",
                nullable: false,
                defaultValue: 1);

            // Los salientes con plantilla anteriores a la cola quedarian como Texto por el valor por
            // defecto, y un reintento los reenviaria como texto libre, posiblemente fuera de la
            // ventana de 24h: exactamente lo que Meta sanciona (Regla 15).
            migrationBuilder.Sql("UPDATE Mensajes SET TipoSaliente = 2 WHERE PlantillaId IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_Mensajes_ClaveIdempotencia",
                table: "Mensajes",
                column: "ClaveIdempotencia",
                unique: true,
                filter: "[ClaveIdempotencia] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Mensajes_EnCola",
                table: "Mensajes",
                columns: new[] { "EstadoEntrega", "FechaEnvio" },
                filter: "[EstadoEntrega] = 6");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Mensajes_ClaveIdempotencia",
                table: "Mensajes");

            migrationBuilder.DropIndex(
                name: "IX_Mensajes_EnCola",
                table: "Mensajes");

            migrationBuilder.DropColumn(
                name: "ClaveIdempotencia",
                table: "Mensajes");

            migrationBuilder.DropColumn(
                name: "FechaTomaEnvio",
                table: "Mensajes");

            migrationBuilder.DropColumn(
                name: "OpcionesJson",
                table: "Mensajes");

            migrationBuilder.DropColumn(
                name: "TipoSaliente",
                table: "Mensajes");
        }
    }
}
