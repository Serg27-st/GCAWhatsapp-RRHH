using RRHH.WhatsApp.Worker;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>Una prueba que necesita un SQL Server de verdad. Se omite si no se indica cual.</summary>
public sealed class FactConSqlServerAttribute : FactAttribute
{
    public const string Variable = "RRHH_PRUEBAS_SQL";

    public FactConSqlServerAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"Necesita SQL Server: definir {Variable} con una cadena de conexion para correrla.";
    }
}

/// <summary>
/// V24 — el candado real. No se puede probar en memoria: lo que importa es como se comporta
/// <c>sp_getapplock</c>, y eso lo decide SQL Server.
/// <para>
/// Cada prueba usa un recurso propio, asi que se puede correr contra la base de desarrollo sin
/// tocar el candado de un Worker que este andando, y sin escribir una sola fila.
/// </para>
/// </summary>
public class CandadoSqlServerTests
{
    private static string Cadena => Environment.GetEnvironmentVariable(FactConSqlServerAttribute.Variable)!;

    private static string RecursoDePrueba() => $"RRHH.Pruebas.{Guid.NewGuid():N}";

    [FactConSqlServer]
    public async Task Dos_instancias_no_toman_el_mismo_candado()
    {
        var recurso = RecursoDePrueba();

        await using var activa = new CandadoSqlServer(Cadena, recurso);
        await using var reserva = new CandadoSqlServer(Cadena, recurso);

        Assert.True(await activa.IntentarTomarAsync(default));
        Assert.False(await reserva.IntentarTomarAsync(default));

        Assert.True(await activa.SigueTomadoAsync(default));
        Assert.False(await reserva.SigueTomadoAsync(default));
    }

    [FactConSqlServer]
    public async Task Al_liberarlo_lo_toma_la_de_reserva()
    {
        var recurso = RecursoDePrueba();

        await using var activa = new CandadoSqlServer(Cadena, recurso);
        await using var reserva = new CandadoSqlServer(Cadena, recurso);

        Assert.True(await activa.IntentarTomarAsync(default));
        Assert.False(await reserva.IntentarTomarAsync(default));

        await activa.LiberarAsync();

        Assert.True(await reserva.IntentarTomarAsync(default));
        Assert.False(await activa.SigueTomadoAsync(default));
    }

    /// <summary>
    /// El caso por el que se eligio un candado de sesion y una conexion sin pool: el Worker muere
    /// sin llegar a liberarlo, y aun asi la reserva puede tomar el relevo.
    /// </summary>
    [FactConSqlServer]
    public async Task Si_la_activa_muere_sin_liberarlo_se_suelta_solo()
    {
        var recurso = RecursoDePrueba();

        var activa = new CandadoSqlServer(Cadena, recurso);
        await using var reserva = new CandadoSqlServer(Cadena, recurso);

        Assert.True(await activa.IntentarTomarAsync(default));

        // Cerrar sin sp_releaseapplock: es lo que pasa cuando el proceso muere.
        await activa.DisposeAsync();

        Assert.True(await reserva.IntentarTomarAsync(default));
    }
}
