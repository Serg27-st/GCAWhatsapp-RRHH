using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RRHH.WhatsApp.Infrastructure.Persistencia;

/// <summary>
/// Permite ejecutar <c>dotnet ef migrations</c> sobre este proyecto sin arrancar la Api.
/// Solo se usa en tiempo de diseno; en ejecucion la cadena de conexion viene de la configuracion
/// del host (Seccion 9.6.1: los secretos viven fuera del codigo).
/// </summary>
public class RrhhDbContextFactory : IDesignTimeDbContextFactory<RrhhDbContext>
{
    private const string CadenaPorDefecto =
        @"Server=.\SQLEXPRESS;Database=RRHH_WhatsApp;Trusted_Connection=True;TrustServerCertificate=True";

    public RrhhDbContext CreateDbContext(string[] args)
    {
        var cadena = Environment.GetEnvironmentVariable("RRHH_CONNECTIONSTRING") ?? CadenaPorDefecto;

        var opciones = new DbContextOptionsBuilder<RrhhDbContext>()
            .UseSqlServer(cadena, sql => sql.MigrationsAssembly(typeof(RrhhDbContextFactory).Assembly.FullName))
            .Options;

        return new RrhhDbContext(opciones);
    }
}
