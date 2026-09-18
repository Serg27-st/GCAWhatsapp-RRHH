using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RRHH.WhatsApp.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class SeguimientoConversacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FechaAvisoFueraHorario",
                table: "Conversaciones",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaAvisoPendiente",
                table: "Conversaciones",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaAvisoSegundoNivel",
                table: "Conversaciones",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaEscalamiento",
                table: "Conversaciones",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaPendienteDesde",
                table: "Conversaciones",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaTextoNoReconocido",
                table: "Conversaciones",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IntentosMenuFallidos",
                table: "Conversaciones",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Conversaciones_EstadoActividad",
                table: "Conversaciones",
                columns: new[] { "Estado", "FechaUltimaActividad" });

            // V30: hasta ahora PendienteClasificar mezclaba lo que el bot atendia con lo que el bot no
            // pudo clasificar. Solo sigue en «Sin clasificar» lo que la Regla 19 derivo de verdad (dejo
            // la auditoria DerivadaABandejaGeneral); el resto vuelve al menu del bot.
            migrationBuilder.Sql("""
                UPDATE c SET Estado = 6
                FROM Conversaciones c
                WHERE c.Estado = 3
                  AND NOT EXISTS (
                    SELECT 1 FROM Auditoria a
                    WHERE a.EntidadTipo = 'Conversacion'
                      AND a.EntidadId = CAST(c.ConversacionId AS nvarchar(50))
                      AND a.Accion = 'DerivadaABandejaGeneral');
                """);

            // P3: sin fecha de entrada no corre el plazo del aviso. La mejor aproximacion disponible es
            // la ultima actividad.
            migrationBuilder.Sql("UPDATE Conversaciones SET FechaPendienteDesde = FechaUltimaActividad WHERE Estado = 3;");

            // No estaba en la especificacion: sin FechaEscalamiento, las conversaciones ya escaladas
            // nunca llegarian al segundo nivel (A9). Se toma del ultimo escalamiento auditado.
            migrationBuilder.Sql("""
                UPDATE c SET FechaEscalamiento = (
                    SELECT MAX(a.Fecha) FROM Auditoria a
                    WHERE a.EntidadTipo = 'Conversacion'
                      AND a.EntidadId = CAST(c.ConversacionId AS nvarchar(50))
                      AND a.Accion = 'Escalamiento')
                FROM Conversaciones c
                WHERE c.Estado = 2;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sin EnMenuBot en el modelo anterior, lo que el bot atendia vuelve a PendienteClasificar.
            migrationBuilder.Sql("UPDATE Conversaciones SET Estado = 3 WHERE Estado = 6;");

            migrationBuilder.DropIndex(
                name: "IX_Conversaciones_EstadoActividad",
                table: "Conversaciones");

            migrationBuilder.DropColumn(
                name: "FechaAvisoFueraHorario",
                table: "Conversaciones");

            migrationBuilder.DropColumn(
                name: "FechaAvisoPendiente",
                table: "Conversaciones");

            migrationBuilder.DropColumn(
                name: "FechaAvisoSegundoNivel",
                table: "Conversaciones");

            migrationBuilder.DropColumn(
                name: "FechaEscalamiento",
                table: "Conversaciones");

            migrationBuilder.DropColumn(
                name: "FechaPendienteDesde",
                table: "Conversaciones");

            migrationBuilder.DropColumn(
                name: "FechaTextoNoReconocido",
                table: "Conversaciones");

            migrationBuilder.DropColumn(
                name: "IntentosMenuFallidos",
                table: "Conversaciones");
        }
    }
}
