using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// Una base de SQL Server propia para una clase de pruebas, creada con las migraciones reales y
/// borrada al terminar (ARQ-12). Lo que EF InMemory no tiene —transacciones, índices únicos
/// filtrados, RowVersion, migraciones de datos— solo se puede probar así.
/// <para>
/// Toma el servidor de <c>RRHH_PRUEBAS_SQL</c> y cambia el nombre de la base por
/// <c>RRHH_Pruebas_{guid}</c>: nunca escribe en la base que indica la variable. Sin la variable no
/// hace nada, y las pruebas con <see cref="FactConSqlServerAttribute"/> se omiten solas.
/// </para>
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly string? _servidor = Environment.GetEnvironmentVariable(FactConSqlServerAttribute.Variable);

    /// <summary>Cadena de la base temporal. Nula si no hay SQL Server configurado.</summary>
    public string? Cadena { get; private set; }

    public bool Disponible => Cadena is not null;

    public RrhhDbContext CrearContexto() => new(new DbContextOptionsBuilder<RrhhDbContext>()
        .UseSqlServer(
            Cadena ?? throw new InvalidOperationException(
                $"Sin SQL Server de pruebas: definir {FactConSqlServerAttribute.Variable}."),
            sql => sql.CommandTimeout(TiempoEsperaComandosSegundos))
        .Options);

    /// <summary>
    /// Crear la base, aplicar todas las migraciones y borrarla es trabajo pesado de disco. En un SQL
    /// Express de desarrollo con poca memoria disponible supera los 30 s por defecto sin que haya nada
    /// mal en el codigo; un tiempo de espera corto convertia eso en pruebas rojas intermitentes.
    /// </summary>
    public const int TiempoEsperaComandosSegundos = 300;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_servidor))
            return;

        Cadena = new SqlConnectionStringBuilder(_servidor)
        {
            InitialCatalog = $"RRHH_Pruebas_{Guid.NewGuid():N}"
        }.ConnectionString;

        await using var db = CrearContexto();

        // MigrateAsync y no EnsureCreated: lo que se prueba es el esquema que llega a producción,
        // incluidas las migraciones de datos (V17 ya documentó una que borraba un índice por error).
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (Cadena is null)
            return;

        // Las conexiones del pool mantienen la base en uso y el DROP falla. Se limpian primero.
        SqlConnection.ClearAllPools();

        await using var db = CrearContexto();
        await db.Database.EnsureDeletedAsync();
    }
}
