using System.Data;
using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Infrastructure.Persistencia;

/// <summary>
/// <see cref="IUnidadTrabajo"/> sobre el <see cref="RrhhDbContext"/> del ambito (V28, ARQ-02).
/// <para>
/// No es una capa de repositorio (V6): no envuelve el DbContext, solo le pone una frontera
/// transaccional a un caso de uso. Los servicios siguen llamando a SaveChanges y la transaccion
/// ambiental decide al final, asi que no hubo que reescribirlos para quitarles el guardado.
/// </para>
/// <para>
/// La transaccion manual es valida porque <c>UseSqlServer</c> no lleva <c>EnableRetryOnFailure</c>.
/// Si algun dia se activa, el trabajo tiene que ir dentro de <c>Database.CreateExecutionStrategy()</c>:
/// una estrategia de reintento no admite transacciones iniciadas por fuera de ella.
/// </para>
/// </summary>
public sealed class UnidadTrabajoEf(RrhhDbContext db) : IUnidadTrabajo
{
    public Task EjecutarAsync(Func<CancellationToken, Task> trabajo, CancellationToken ct = default) =>
        EjecutarAsync(async c =>
        {
            await trabajo(c);
            return true;
        }, ct);

    public async Task<T> EjecutarAsync<T>(Func<CancellationToken, Task<T>> trabajo, CancellationToken ct = default)
    {
        // Anidada: la confirma quien la abrio. Confirmarla aca le quitaria al caso de uso externo la
        // posibilidad de deshacer todo si falla despues.
        if (db.Database.CurrentTransaction is not null)
            return await trabajo(ct);

        // ReadCommitted es el nivel por defecto de SQL Server; se declara para que no dependa de la
        // configuracion del servidor. Con un solo Worker (V24) no hace falta uno mas estricto.
        await using var transaccion = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        try
        {
            var resultado = await trabajo(ct);

            await transaccion.CommitAsync(ct);

            return resultado;
        }
        catch
        {
            // Sin el token del llamador: si lo que fallo fue una cancelacion, el rollback igual
            // tiene que llegar a la base.
            await transaccion.RollbackAsync(CancellationToken.None);

            // El rastreador todavia tiene las entidades del trabajo deshecho. Si quedaran, el
            // proximo SaveChanges del mismo ambito las confirmaria por fuera de la transaccion.
            db.ChangeTracker.Clear();

            throw;
        }
    }
}
