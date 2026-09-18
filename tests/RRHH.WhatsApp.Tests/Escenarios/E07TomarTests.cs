using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Excepciones;
using RRHH.WhatsApp.Infrastructure.Servicios;
using RRHH.WhatsApp.Tests.Infraestructura;

namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// E07 (FUN-01, AL1): dos analistas toman el mismo hilo a la vez y solo uno se lo queda. Se prueba
/// contra SQL Server porque lo que resuelve la carrera es el <c>RowVersion</c> de la fila, que EF
/// InMemory no tiene.
/// </summary>
public class E07TomarTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    private static ConversacionService Servicio(Infrastructure.Persistencia.RrhhDbContext db) =>
        ServiciosDePrueba.Conversaciones(db);

    [FactConSqlServer]
    public async Task Dos_analistas_toman_a_la_vez_y_solo_uno_se_la_queda()
    {
        int conversacionId, cuentaId, unoId, otroId;

        await using (var db = sql.CrearContexto())
        {
            var sufijo = Guid.NewGuid().ToString("N")[..8];

            var uno = new Analista { Nombre = "Uno", Email = $"uno.{sufijo}@gca.pe", Activo = true };
            var otro = new Analista { Nombre = "Otro", Email = $"otro.{sufijo}@gca.pe", Activo = true };
            var cuenta = new Cuenta { Nombre = $"Cuenta {sufijo}", Activo = true };

            db.Analistas.AddRange(uno, otro);
            db.Cuentas.Add(cuenta);
            await db.SaveChangesAsync();

            // Los dos trabajan la cuenta: la carrera es por el hilo, no por el permiso.
            db.AnalistaCuentas.AddRange(
                new AnalistaCuenta { AnalistaId = uno.AnalistaId, CuentaId = cuenta.CuentaId, EsBackup = false },
                new AnalistaCuenta { AnalistaId = otro.AnalistaId, CuentaId = cuenta.CuentaId, EsBackup = true });

            var conversacion = await Servicio(db)
                .ObtenerOCrearAsync($"+519{Random.Shared.Next(10000000, 99999999)}");

            conversacion.Estado = EstadoConversacion.PendienteClasificar;
            conversacion.FechaPendienteDesde = DateTime.UtcNow.AddHours(-1);

            await db.SaveChangesAsync();

            (conversacionId, cuentaId, unoId, otroId) =
                (conversacion.ConversacionId, cuenta.CuentaId, uno.AnalistaId, otro.AnalistaId);
        }

        var intentos = new[] { unoId, otroId }.Select(async analistaId =>
        {
            await using var db = sql.CrearContexto();

            try
            {
                await Servicio(db).TomarAsync(conversacionId, analistaId, cuentaId);
                return (Analista: analistaId, Error: (string?)null);
            }
            catch (ConflictoConcurrenciaException ex)
            {
                return (Analista: analistaId, Error: ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                // El otro guardo primero y este releyo la fila ya tomada: mismo desenlace visible.
                return (Analista: analistaId, Error: ex.Message);
            }
        });

        var resultados = await Task.WhenAll(intentos);

        var ganador = Assert.Single(resultados, r => r.Error is null);
        Assert.Single(resultados, r => r.Error is not null);

        await using var verificacion = sql.CrearContexto();

        var final = await verificacion.Conversaciones.AsNoTracking()
            .FirstAsync(c => c.ConversacionId == conversacionId);

        Assert.Equal(EstadoConversacion.Activa, final.Estado);
        Assert.Equal(ganador.Analista, final.AnalistaAtendiendoId);
        Assert.Null(final.FechaPendienteDesde);
    }
}
