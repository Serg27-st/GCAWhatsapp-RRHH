using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Servicios;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// El cambio de titular contra el indice unico real. En memoria no hay indices, asi que el error que
/// esto corrige —dos titulares por un instante, rechazados por SQL Server— solo se ve aca.
/// <para>
/// Corre dentro de una transaccion que se deshace al final: se puede apuntar a la base de
/// desarrollo sin dejar cuentas ni analistas de prueba.
/// </para>
/// </summary>
public class AsignacionCuentasSqlServerTests
{
    private static string Cadena => Environment.GetEnvironmentVariable(FactConSqlServerAttribute.Variable)!;

    [FactConSqlServer]
    public async Task El_respaldo_pasa_a_titular_sin_chocar_con_el_indice_unico()
    {
        await using var db = new RrhhDbContext(new DbContextOptionsBuilder<RrhhDbContext>()
            .UseSqlServer(Cadena)
            .Options);

        await using var transaccion = await db.Database.BeginTransactionAsync();

        var cuentas = new CuentaService(db, TimeProvider.System);
        var analistas = new AnalistaService(db);
        var sufijo = Guid.NewGuid().ToString("N")[..8];

        var cuenta = await cuentas.CrearAsync($"Prueba {sufijo}");
        var titular = await analistas.CrearAsync("Titular", $"titular.{sufijo}@prueba.pe", RolAnalista.Analista);
        var respaldo = await analistas.CrearAsync("Respaldo", $"respaldo.{sufijo}@prueba.pe", RolAnalista.Analista);

        await cuentas.AsignarAnalistaAsync(cuenta.CuentaId, titular.AnalistaId, esBackup: false);
        await cuentas.AsignarAnalistaAsync(cuenta.CuentaId, respaldo.AnalistaId, esBackup: true);

        await cuentas.AsignarAnalistaAsync(cuenta.CuentaId, respaldo.AnalistaId, esBackup: false);

        var fila = Assert.Single(await db.AnalistaCuentas.Where(ac => ac.CuentaId == cuenta.CuentaId).ToListAsync());

        Assert.Equal(respaldo.AnalistaId, fila.AnalistaId);
        Assert.False(fila.EsBackup);

        await transaccion.RollbackAsync();
    }
}
