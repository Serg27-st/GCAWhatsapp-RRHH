using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RRHH.WhatsApp.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class VencimientoTransferencias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FechaVencimiento",
                table: "Transferencias",
                type: "datetime2",
                nullable: true);

            // El indice exige una sola pendiente por conversacion. Si quedaron dos de antes (la
            // comprobacion del servicio no veia pedidos simultaneos), se deja la mas reciente y las
            // demas se dan por rechazadas, con fecha de respuesta, para que el historial lo muestre.
            migrationBuilder.Sql("""
                WITH pendientes AS (
                    SELECT TransferenciaId,
                           ROW_NUMBER() OVER (PARTITION BY ConversacionId ORDER BY Fecha DESC, TransferenciaId DESC) AS orden
                    FROM Transferencias
                    WHERE Estado = 1)
                UPDATE Transferencias
                SET Estado = 3, FechaRespuesta = SYSUTCDATETIME()
                WHERE TransferenciaId IN (SELECT TransferenciaId FROM pendientes WHERE orden > 1);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Transferencias_PendienteUnica",
                table: "Transferencias",
                column: "ConversacionId",
                unique: true,
                filter: "[Estado] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transferencias_PendienteUnica",
                table: "Transferencias");

            migrationBuilder.DropColumn(
                name: "FechaVencimiento",
                table: "Transferencias");
        }
    }
}
