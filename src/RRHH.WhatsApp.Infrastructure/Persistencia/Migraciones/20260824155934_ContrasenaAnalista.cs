using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RRHH.WhatsApp.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class ContrasenaAnalista : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FechaContrasena",
                table: "Analistas",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HashContrasena",
                table: "Analistas",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FechaContrasena",
                table: "Analistas");

            migrationBuilder.DropColumn(
                name: "HashContrasena",
                table: "Analistas");
        }
    }
}
