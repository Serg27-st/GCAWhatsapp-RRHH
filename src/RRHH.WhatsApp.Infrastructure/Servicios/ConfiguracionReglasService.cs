using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Infrastructure.Servicios;

/// <summary>
/// Lee los parametros ajustables sin redeploy. Se cachean unos segundos porque el motor de reglas
/// los pide en cada evaluacion, y cambian una vez cada varios meses; la ventana corta hace que un
/// ajuste desde la administracion se note casi de inmediato sin reiniciar nada.
/// </summary>
public sealed class ConfiguracionReglasService(RrhhDbContext db, IMemoryCache cache) : IConfiguracionReglasService
{
    private const string ClaveCache = "configuracion-reglas";
    private static readonly TimeSpan Vigencia = TimeSpan.FromSeconds(30);

    public async Task<IReadOnlyDictionary<string, string>> ObtenerTodasAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue<IReadOnlyDictionary<string, string>>(ClaveCache, out var cacheado) && cacheado is not null)
            return cacheado;

        var valores = await db.ConfiguracionReglas
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Clave, c => c.Valor, ct);

        cache.Set(ClaveCache, (IReadOnlyDictionary<string, string>)valores, Vigencia);

        return valores;
    }

    public async Task EstablecerAsync(string clave, string valor, CancellationToken ct = default)
    {
        var existente = await db.ConfiguracionReglas.FirstOrDefaultAsync(c => c.Clave == clave, ct);

        if (existente is null)
        {
            db.ConfiguracionReglas.Add(new ConfiguracionRegla
            {
                Clave = clave,
                Valor = valor,
                FechaActualizacion = DateTime.UtcNow
            });
        }
        else
        {
            existente.Valor = valor;
            existente.FechaActualizacion = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        cache.Remove(ClaveCache);
    }
}
