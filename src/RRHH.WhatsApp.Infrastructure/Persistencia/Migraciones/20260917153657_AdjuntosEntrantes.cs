using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RRHH.WhatsApp.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class AdjuntosEntrantes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MensajesAdjuntos",
                columns: table => new
                {
                    AdjuntoId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MensajeId = table.Column<long>(type: "bigint", nullable: false),
                    TipoMedio = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProveedorMedioId = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    MimeType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NombreArchivo = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    TamanoBytes = table.Column<long>(type: "bigint", nullable: true),
                    Ruta = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IntentosDescarga = table.Column<int>(type: "int", nullable: false),
                    ProximoIntentoUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FechaRecepcion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MensajesAdjuntos", x => x.AdjuntoId);
                    table.ForeignKey(
                        name: "FK_MensajesAdjuntos_Mensajes_MensajeId",
                        column: x => x.MensajeId,
                        principalTable: "Mensajes",
                        principalColumn: "MensajeId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MensajesAdjuntos_Estado_FechaRecepcion",
                table: "MensajesAdjuntos",
                columns: new[] { "Estado", "FechaRecepcion" });

            migrationBuilder.CreateIndex(
                name: "IX_MensajesAdjuntos_MensajeId",
                table: "MensajesAdjuntos",
                column: "MensajeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MensajesAdjuntos");
        }
    }
}
