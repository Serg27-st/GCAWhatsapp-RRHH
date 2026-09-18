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

        // Tiempo de espera amplio solo para las herramientas: una migracion con migracion de datos sobre
        // un SQL Express con poca memoria supera los 30 s por defecto, y un tiempo de espera a mitad
        // obliga a revertir y reaplicar.
        var opciones = new DbContextOptionsBuilder<RrhhDbContext>()
            .UseSqlServer(cadena, sql => sql
                .MigrationsAssembly(typeof(RrhhDbContextFactory).Assembly.FullName)
                .CommandTimeout(300))
            .Options;

        return new RrhhDbContext(opciones);
    }
}
