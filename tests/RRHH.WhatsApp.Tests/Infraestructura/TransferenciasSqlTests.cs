using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Infrastructure.Servicios;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// T2.08 (Regla 8, A1): una sola transferencia pendiente por conversación, garantizada por el índice
/// <c>IX_Transferencias_PendienteUnica</c>. La comprobación del servicio no ve dos pedidos simultáneos;
/// sin el índice, dos analistas podían derivar el mismo hilo a dos destinos a la vez.
/// </summary>
public class TransferenciasSqlTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    [FactConSqlServer]
    public async Task Dos_transferencias_simultaneas_de_la_misma_conversacion_una_falla_con_mensaje_de_negocio()
    {
        int conversacionId, origenId, destinoA, destinoB;

        await using (var db = sql.CrearContexto())
        {
            var sufijo = Guid.NewGuid().ToString("N")[..8];
            var origen = new Analista { Nombre = "Origen", Email = $"origen.{sufijo}@gca.pe", Activo = true };
            var a = new Analista { Nombre = "Destino A", Email = $"a.{sufijo}@gca.pe", Activo = true };
            var b = new Analista { Nombre = "Destino B", Email = $"b.{sufijo}@gca.pe", Activo = true };
            db.Analistas.AddRange(origen, a, b);
            await db.SaveChangesAsync();

            var conversacion = await ServiciosDePrueba.Conversaciones(db, TimeProvider.System)
                .ObtenerOCrearAsync($"+519{Random.Shared.Next(10000000, 99999999)}");

            // El hilo nace en el menu del bot (V30) y desde ahi no se transfiere: primero lo toma
            // alguien (FUN-01). Se deja como queda despues de tomarlo.
            conversacion.Estado = EstadoConversacion.Activa;
            conversacion.AnalistaAtendiendoId = origen.AnalistaId;

            await db.SaveChangesAsync();

            (conversacionId, origenId, destinoA, destinoB) = (conversacion.ConversacionId, origen.AnalistaId, a.AnalistaId, b.AnalistaId);
        }

        // Varias vueltas: la carrera no siempre se da, pero en ninguna pueden quedar dos pendientes.
        for (var vuelta = 0; vuelta < 5; vuelta++)
        {
            var intentos = new[] { destinoA, destinoB }.Select(async destino =>
            {
                await using var db = sql.CrearContexto();
                var servicio = ServiciosDePrueba.Conversaciones(db, TimeProvider.System);

                try
                {
                    await servicio.TransferirAsync(conversacionId, origenId, destino, urgente: false, comentario: null);
                    return (string?)null;
                }
                catch (InvalidOperationException ex)
                {
                    return ex.Message;
                }
            });

            var resultados = await Task.WhenAll(intentos);

            Assert.Single(resultados, r => r is null);
            Assert.Single(resultados, r => r == "Ya hay una transferencia pendiente de respuesta para esta conversacion.");

            await using var verificacion = sql.CrearContexto();
            Assert.Equal(1, await verificacion.Transferencias.CountAsync(t => t.ConversacionId == conversacionId && t.Estado == EstadoTransferencia.Pendiente));

            // Se libera para la vuelta siguiente, como si el destino la hubiera rechazado.
            await verificacion.Transferencias
                .Where(t => t.ConversacionId == conversacionId && t.Estado == EstadoTransferencia.Pendiente)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Estado, EstadoTransferencia.Rechazada));
        }
    }
}
