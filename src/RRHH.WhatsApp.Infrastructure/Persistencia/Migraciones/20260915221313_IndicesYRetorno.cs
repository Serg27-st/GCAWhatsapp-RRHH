using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RRHH.WhatsApp.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class IndicesYRetorno : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // B9: el indice unico de abajo falla si ya hay dos respuestas para la misma invitacion. No se
            // eligen ni se borran automaticamente: son datos personales de un postulante (Regla 17) y
            // decidir cual vale es de una persona. Se detiene la migracion con el detalle para revisarlo.
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1 FROM JobFormsRespuestas
                    WHERE InvitacionId IS NOT NULL
                    GROUP BY InvitacionId
                    HAVING COUNT(*) > 1)
                BEGIN
                    DECLARE @invitaciones nvarchar(2000) = (
                        SELECT STRING_AGG(CAST(InvitacionId AS nvarchar(20)), ', ')
                        FROM (SELECT InvitacionId FROM JobFormsRespuestas
                              WHERE InvitacionId IS NOT NULL
                              GROUP BY InvitacionId HAVING COUNT(*) > 1) d);

                    DECLARE @mensaje nvarchar(2048) = CONCAT(
                        'IndicesYRetorno: hay respuestas duplicadas del JobForms para las invitaciones ', @invitaciones,
                        '. Revisar a mano cual conservar antes de aplicar esta migracion (T2.09).');

                    THROW 50001, @mensaje, 1;
                END
                """);

            migrationBuilder.DropIndex(
                name: "IX_JobFormsRespuestas_InvitacionId",
                table: "JobFormsRespuestas");

            migrationBuilder.DropIndex(
                name: "IX_EventosSistema_Estado_FechaCreacion",
                table: "EventosSistema");

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaAvisoRetorno",
                table: "Ausencias",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobFormsRespuestas_Invitacion",
                table: "JobFormsRespuestas",
                column: "InvitacionId",
                unique: true,
                filter: "[InvitacionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EventosSistema_Cola",
                table: "EventosSistema",
                columns: new[] { "Estado", "Tipo", "FechaCreacion" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JobFormsRespuestas_Invitacion",
                table: "JobFormsRespuestas");

            migrationBuilder.DropIndex(
                name: "IX_EventosSistema_Cola",
                table: "EventosSistema");

            migrationBuilder.DropColumn(
                name: "FechaAvisoRetorno",
                table: "Ausencias");

            migrationBuilder.CreateIndex(
                name: "IX_JobFormsRespuestas_InvitacionId",
                table: "JobFormsRespuestas",
                column: "InvitacionId");

            migrationBuilder.CreateIndex(
                name: "IX_EventosSistema_Estado_FechaCreacion",
                table: "EventosSistema",
                columns: new[] { "Estado", "FechaCreacion" });
        }
    }
}
