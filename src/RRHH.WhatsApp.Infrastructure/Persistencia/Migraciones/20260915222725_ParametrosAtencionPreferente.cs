using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace RRHH.WhatsApp.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class ParametrosAtencionPreferente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "ConfiguracionReglas",
                keyColumn: "Clave",
                keyValue: "envio.maximo_por_segundo",
                column: "Descripcion",
                value: "Seccion 9.6.4: tope de velocidad de envio saliente hacia Meta, por proceso emisor, para no repetir el patron que causo el bloqueo.");

            migrationBuilder.InsertData(
                table: "ConfiguracionReglas",
                columns: new[] { "Clave", "Descripcion", "FechaActualizacion", "Valor" },
                values: new object[,]
                {
                    { "cierre.automatico", "Regla 12 (A11): el cierre de cortesia sale por defecto al descartar; el analista puede marcar no enviar.", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "true" },
                    { "clasificacion.horas_aviso", "Regla 19 (P3): horas habiles en Sin clasificar antes de avisar a Jefatura.", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "2" },
                    { "conversacion.aviso_archivado_dias", "Regla 16 (A14): dias antes del archivado en que se avisa al analista.", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "7" },
                    { "datos.retencion_adjuntos_dias", "Regla 17: dias de retencion de los adjuntos que llegan por WhatsApp.", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "365" },
                    { "escalamiento.horas_segundo_nivel", "Regla 2 (A9): horas habiles desde el escalamiento sin respuesta del respaldo antes de avisar a Jefatura.", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "2" },
                    { "menu.horas_derivacion", "Regla 19 (A12): horas habiles de silencio tras un texto no reconocido antes de derivar a Sin clasificar.", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "2" },
                    { "menu.reintentos_permitidos", "Regla 19: reintentos del menu sin opcion valida antes de derivar a Sin clasificar.", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "1" },
                    { "outbox.retencion_dias_procesados", "Seccion 9.6.2: dias que se conservan los eventos ya procesados de la outbox antes de purgarlos.", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "30" },
                    { "transferencia.horas_vencimiento", "Regla 8 (A1): horas habiles hasta que vence una transferencia no urgente sin respuesta.", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "2" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ConfiguracionReglas",
                keyColumn: "Clave",
                keyValue: "cierre.automatico");

            migrationBuilder.DeleteData(
                table: "ConfiguracionReglas",
                keyColumn: "Clave",
                keyValue: "clasificacion.horas_aviso");

            migrationBuilder.DeleteData(
                table: "ConfiguracionReglas",
                keyColumn: "Clave",
                keyValue: "conversacion.aviso_archivado_dias");

            migrationBuilder.DeleteData(
                table: "ConfiguracionReglas",
                keyColumn: "Clave",
                keyValue: "datos.retencion_adjuntos_dias");

            migrationBuilder.DeleteData(
                table: "ConfiguracionReglas",
                keyColumn: "Clave",
                keyValue: "escalamiento.horas_segundo_nivel");

            migrationBuilder.DeleteData(
                table: "ConfiguracionReglas",
                keyColumn: "Clave",
                keyValue: "menu.horas_derivacion");

            migrationBuilder.DeleteData(
                table: "ConfiguracionReglas",
                keyColumn: "Clave",
                keyValue: "menu.reintentos_permitidos");

            migrationBuilder.DeleteData(
                table: "ConfiguracionReglas",
                keyColumn: "Clave",
                keyValue: "outbox.retencion_dias_procesados");

            migrationBuilder.DeleteData(
                table: "ConfiguracionReglas",
                keyColumn: "Clave",
                keyValue: "transferencia.horas_vencimiento");

            migrationBuilder.UpdateData(
                table: "ConfiguracionReglas",
                keyColumn: "Clave",
                keyValue: "envio.maximo_por_segundo",
                column: "Descripcion",
                value: "Seccion 9.6.4: tope de velocidad de envio saliente hacia Meta, para no repetir el patron que causo el bloqueo.");
        }
    }
}
