using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RRHH.WhatsApp.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class AlertasOperativas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AlertasOperativas",
                columns: table => new
                {
                    AlertaId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Tipo = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Clave = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Detalle = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Ocurrencias = table.Column<int>(type: "int", nullable: false),
                    FechaPrimera = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaUltima = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaResuelta = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResueltaPorAnalistaId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertasOperativas", x => x.AlertaId);
                    table.ForeignKey(
                        name: "FK_AlertasOperativas_Analistas_ResueltaPorAnalistaId",
                        column: x => x.ResueltaPorAnalistaId,
                        principalTable: "Analistas",
                        principalColumn: "AnalistaId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlertasOperativas_AbiertaUnica",
                table: "AlertasOperativas",
                columns: new[] { "Tipo", "Clave" },
                unique: true,
                filter: "[FechaResuelta] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AlertasOperativas_ResueltaPorAnalistaId",
                table: "AlertasOperativas",
                column: "ResueltaPorAnalistaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertasOperativas");
        }
    }
}
