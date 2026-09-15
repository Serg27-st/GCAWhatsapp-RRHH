using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// V28 (ARQ-02): un caso de uso se confirma o se deshace entero. Sin esto, cada servicio hacía su
/// propio SaveChanges y un fallo a mitad dejaba escrita la primera parte (C5, C6).
/// <para>
/// En memoria no hay transacciones —EF las ignora—, así que ahí solo se prueba lo que no depende de
/// la base: que la excepción sale y que el rastreador queda limpio. Lo que se deshace de verdad se
/// prueba contra SQL Server con <c>RRHH_PRUEBAS_SQL</c>.
/// </para>
/// </summary>
public class UnidadTrabajoEfTests
{
    private static RrhhDbContext EnMemoria() => new(new DbContextOptionsBuilder<RrhhDbContext>()
        .UseInMemoryDatabase($"unidad-{Guid.NewGuid()}")
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private static string Cadena => Environment.GetEnvironmentVariable(FactConSqlServerAttribute.Variable)!;

    private static RrhhDbContext EnSqlServer() => new(new DbContextOptionsBuilder<RrhhDbContext>()
        .UseSqlServer(Cadena)
        .Options);

    [Fact]
    public async Task Un_trabajo_que_falla_propaga_la_excepcion_y_limpia_el_rastreador()
    {
        await using var db = EnMemoria();
        var unidad = new UnidadTrabajoEf(db);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            unidad.EjecutarAsync(_ =>
            {
                db.Cuentas.Add(new Cuenta { Nombre = "A medias" });
                throw new InvalidOperationException("falla a mitad del caso de uso");
            }));

        Assert.Equal("falla a mitad del caso de uso", error.Message);

        // Un rastreador con la entidad a medias la confirmaría en el próximo SaveChanges del
        // mismo ámbito, que es justo lo que la unidad de trabajo existe para impedir.
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Un_trabajo_que_termina_devuelve_su_resultado()
    {
        await using var db = EnMemoria();
        var unidad = new UnidadTrabajoEf(db);

        var id = await unidad.EjecutarAsync(async ct =>
        {
            var cuenta = new Cuenta { Nombre = "Confirmada" };
            db.Cuentas.Add(cuenta);
            await db.SaveChangesAsync(ct);
            return cuenta.CuentaId;
        });

        Assert.True(await db.Cuentas.AnyAsync(c => c.CuentaId == id));
    }

    [FactConSqlServer]
    public async Task Contra_SQL_Server_lo_que_ya_se_guardo_se_deshace_si_el_trabajo_falla()
    {
        var nombre = $"Prueba unidad {Guid.NewGuid():N}"[..40];

        await using (var db = EnSqlServer())
        {
            var unidad = new UnidadTrabajoEf(db);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                unidad.EjecutarAsync(async ct =>
                {
                    db.Cuentas.Add(new Cuenta { Nombre = nombre });

                    // El SaveChanges intermedio es el patrón real: los servicios siguen guardando
                    // por su cuenta, y la transacción ambiental es la que decide al final.
                    await db.SaveChangesAsync(ct);

                    throw new InvalidOperationException("falla después de guardar");
                }));
        }

        await using var otra = EnSqlServer();

        Assert.False(await otra.Cuentas.AnyAsync(c => c.Nombre == nombre));
    }

    [FactConSqlServer]
    public async Task Contra_SQL_Server_reutiliza_la_transaccion_ya_abierta_sin_confirmarla()
    {
        await using var db = EnSqlServer();
        await using var externa = await db.Database.BeginTransactionAsync();

        var unidad = new UnidadTrabajoEf(db);

        await unidad.EjecutarAsync(ct =>
        {
            // Anidada: quien abrió la transacción es quien la confirma. Si la unidad abriera otra
            // o confirmara esta, el caso de uso externo perdería su capacidad de deshacer todo.
            Assert.Same(externa, db.Database.CurrentTransaction);
            return Task.CompletedTask;
        });

        Assert.Same(externa, db.Database.CurrentTransaction);

        await externa.RollbackAsync();
    }
}
