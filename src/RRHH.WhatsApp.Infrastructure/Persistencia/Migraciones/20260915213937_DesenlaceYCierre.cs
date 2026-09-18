using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RRHH.WhatsApp.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class DesenlaceYCierre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CierreCortesiaPendiente",
                table: "Postulaciones",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaAvisoArchivado",
                table: "Postulaciones",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaCierreCortesia",
                table: "Postulaciones",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaReingreso",
                table: "Postulaciones",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EstadoResultante",
                table: "EtapasKanban",
                type: "int",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "EtapasKanban",
                keyColumn: "EtapaId",
                keyValue: 1,
                column: "EstadoResultante",
                value: null);

            migrationBuilder.UpdateData(
                table: "EtapasKanban",
                keyColumn: "EtapaId",
                keyValue: 2,
                column: "EstadoResultante",
                value: null);

            migrationBuilder.UpdateData(
                table: "EtapasKanban",
                keyColumn: "EtapaId",
                keyValue: 3,
                column: "EstadoResultante",
                value: null);

            migrationBuilder.UpdateData(
                table: "EtapasKanban",
                keyColumn: "EtapaId",
                keyValue: 4,
                column: "EstadoResultante",
                value: 2);

            migrationBuilder.UpdateData(
                table: "EtapasKanban",
                keyColumn: "EtapaId",
                keyValue: 5,
                column: "EstadoResultante",
                value: 3);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CierreCortesiaPendiente",
                table: "Postulaciones");

            migrationBuilder.DropColumn(
                name: "FechaAvisoArchivado",
                table: "Postulaciones");

            migrationBuilder.DropColumn(
                name: "FechaCierreCortesia",
                table: "Postulaciones");

            migrationBuilder.DropColumn(
                name: "FechaReingreso",
                table: "Postulaciones");

            migrationBuilder.DropColumn(
                name: "EstadoResultante",
                table: "EtapasKanban");
        }
    }
}
