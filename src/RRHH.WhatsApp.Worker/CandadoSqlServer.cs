using System.Data;
using Microsoft.Data.SqlClient;

namespace RRHH.WhatsApp.Worker;

/// <summary>
/// Candado de instancia sobre SQL Server, con <c>sp_getapplock</c> en modo sesion (V24).
/// <para>
/// Se eligio un candado de la base y no un mutex o un archivo porque es lo unico que ven todos los
/// Workers posibles: dos procesos en la misma maquina, o en dos servidores, apuntan a la misma base.
/// Y SQL Server lo suelta solo cuando la sesion termina, asi que un Worker que muere —o que se
/// cuelga y alguien lo mata— no deja el candado tomado para siempre.
/// </para>
/// </summary>
public sealed class CandadoSqlServer : ICandadoInstancia
{
    /// <summary>Nombre del candado. Es por base: dos ambientes en el mismo servidor no se pisan.</summary>
    public const string RecursoWorker = "RRHH.WhatsApp.Worker";

    /// <summary>Tope de cada consulta al candado. Una base que tarda mas que esto se da por perdida.</summary>
    private const int TimeoutConsultaSegundos = 10;

    private readonly string _cadena;
    private readonly string _recurso;
    private SqlConnection? _conexion;

    public CandadoSqlServer(string cadenaConexion, string recurso)
    {
        // Sin pool a proposito. Con pool, cerrar la conexion la devuelve con la sesion viva, y el
        // candado queda tomado por una conexion que nadie usa. Sin pool, cerrar es terminar la
        // sesion, y terminarla es soltar el candado.
        _cadena = new SqlConnectionStringBuilder(cadenaConexion)
        {
            Pooling = false,
            ApplicationName = "RRHH.WhatsApp.Worker (candado)"
        }.ConnectionString;

        _recurso = recurso;
    }

    public async Task<bool> IntentarTomarAsync(CancellationToken ct)
    {
        var conexion = await ConexionAbiertaAsync(ct);

        await using var comando = Comando(conexion, """
            DECLARE @resultado int;
            EXEC @resultado = sp_getapplock
                @Resource = @recurso, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0;
            SELECT @resultado;
            """);

        // LockTimeout 0: no se hace cola dentro de SQL Server. Quien espera es la guardia, que
        // reintenta a su ritmo y se puede cancelar al apagar el servicio.
        var resultado = Convert.ToInt32(await comando.ExecuteScalarAsync(ct));

        // 0 y 1 son concedido; -1, lo tiene otra sesion. Lo demas es un error de SQL Server.
        return resultado switch
        {
            >= 0 => true,
            -1 => false,
            _ => throw new InvalidOperationException($"sp_getapplock devolvio {resultado} para '{_recurso}'.")
        };
    }

    public async Task<bool> SigueTomadoAsync(CancellationToken ct)
    {
        if (_conexion is not { State: ConnectionState.Open } conexion)
            return false;

        try
        {
            await using var comando = Comando(conexion, "SELECT APPLOCK_MODE('public', @recurso, 'Session');");

            return await comando.ExecuteScalarAsync(ct) is string modo && modo == "Exclusive";
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Se cayo la conexion: con ella se fue la sesion, y con la sesion el candado.
            return false;
        }
    }

    public async Task LiberarAsync()
    {
        if (_conexion is { State: ConnectionState.Open } conexion)
        {
            try
            {
                await using var comando = Comando(conexion,
                    "EXEC sp_releaseapplock @Resource = @recurso, @LockOwner = 'Session';");

                await comando.ExecuteNonQueryAsync();
            }
            catch (SqlException)
            {
                // No lo tenia, o la conexion ya no servia. En los dos casos cerrarla lo resuelve.
            }
        }

        await DisposeAsync();
    }

    /// <summary>
    /// Cierra sin liberar explicitamente: termina la sesion y SQL Server suelta el candado solo. Es
    /// lo mismo que pasa cuando el proceso muere, y por eso las pruebas lo usan para simularlo.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_conexion is null)
            return;

        await _conexion.DisposeAsync();
        _conexion = null;
    }

    private async Task<SqlConnection> ConexionAbiertaAsync(CancellationToken ct)
    {
        if (_conexion is { State: ConnectionState.Open } abierta)
            return abierta;

        await DisposeAsync();

        _conexion = new SqlConnection(_cadena);
        await _conexion.OpenAsync(ct);

        return _conexion;
    }

    private SqlCommand Comando(SqlConnection conexion, string sql)
    {
        var comando = conexion.CreateCommand();

        comando.CommandText = sql;
        comando.CommandTimeout = TimeoutConsultaSegundos;
        comando.Parameters.Add(new SqlParameter("@recurso", SqlDbType.NVarChar, 255) { Value = _recurso });

        return comando;
    }
}
