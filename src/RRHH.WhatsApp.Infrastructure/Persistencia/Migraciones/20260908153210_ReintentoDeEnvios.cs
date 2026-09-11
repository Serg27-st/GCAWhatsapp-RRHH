using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace RRHH.WhatsApp.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class ReintentoDeEnvios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClaseFallo",
                table: "Mensajes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "IntentosEnvio",
                table: "Mensajes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ParametrosPlantillaJson",
                table: "Mensajes",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProximoIntentoUtc",
                table: "Mensajes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.InsertData(
                table: "ConfiguracionReglas",
                columns: new[] { "Clave", "Descripcion", "FechaActualizacion", "Valor" },
                values: new object[,]
                {
                    { "envio.reintento_base_segundos", "Base del retroceso exponencial entre reintentos de envio: 1m, 2m, 4m.", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "60" },
                    { "envio.reintentos_maximos", "Intentos de un saliente rechazado por causa transitoria antes de darlo por perdido.", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "4" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Mensajes_PendientesDeReintento",
                table: "Mensajes",
                columns: new[] { "EstadoEntrega", "ClaseFallo", "ProximoIntentoUtc" },
                filter: "[ProximoIntentoUtc] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Mensajes_PendientesDeReintento",
                table: "Mensajes");

            migrationBuilder.DeleteData(
                table: "ConfiguracionReglas",
                keyColumn: "Clave",
                keyValue: "envio.reintento_base_segundos");

            migrationBuilder.DeleteData(
                table: "ConfiguracionReglas",
                keyColumn: "Clave",
                keyValue: "envio.reintentos_maximos");

            migrationBuilder.DropColumn(
                name: "ClaseFallo",
                table: "Mensajes");

            migrationBuilder.DropColumn(
                name: "IntentosEnvio",
                table: "Mensajes");

            migrationBuilder.DropColumn(
                name: "ParametrosPlantillaJson",
                table: "Mensajes");

            migrationBuilder.DropColumn(
                name: "ProximoIntentoUtc",
                table: "Mensajes");
        }
    }
}
