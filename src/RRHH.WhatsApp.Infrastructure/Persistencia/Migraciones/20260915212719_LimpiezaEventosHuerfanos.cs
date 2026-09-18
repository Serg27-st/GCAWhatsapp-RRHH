using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RRHH.WhatsApp.Infrastructure.Persistencia.Migraciones
{
    /// <summary>
    /// T1.14 (V32, M1): estos cinco tipos se publicaban en la outbox sin consumidor y quedaban
    /// pendientes para siempre, creciendo contra el tope de SQL Express. Desde esta version son
    /// alertas agrupadas (o, en el caso de EnvioRequierePlantilla, una accion y una auditoria), y
    /// ningun proceso los va a leer nunca. Solo se borran los pendientes: nada los proceso.
    /// </summary>
    public partial class LimpiezaEventosHuerfanos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM EventosSistema
                WHERE Estado = 1
                  AND Tipo IN ('EnvioRequierePlantilla', 'VacanteSinFormulario', 'EnvioOmitidoSinPlantilla',
                               'MenuSinOpciones', 'MenuTruncado');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sin vuelta atras: eran eventos que nadie consumia y no se pueden reconstruir.
        }
    }
}
