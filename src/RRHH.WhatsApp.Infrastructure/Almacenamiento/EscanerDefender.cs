using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Infrastructure.Almacenamiento;

/// <summary>
/// Escanea con Windows Defender por línea de comandos (Sección 9.6.1).
/// <para>
/// Se usa Defender porque ya viene con el servidor: no suma licencia ni servicio que mantener, y
/// el despliegue es on-premise sobre Windows.
/// </para>
/// </summary>
public sealed class EscanerDefender(
    IOptions<OpcionesAntivirus> opciones,
    ILogger<EscanerDefender> log) : IEscanerAntivirus
{
    /// <summary>
    /// Marcador con el que MpCmdRun avisa que la herramienta falló, en vez de dar un veredicto.
    /// <para>
    /// Hace falta mirarlo porque el código de salida no alcanza: MpCmdRun devuelve 2 tanto cuando
    /// encuentra una amenaza como cuando no pudo escanear —por ejemplo con Defender apagado, que
    /// deja "WARN: Product/Feature disabled" en su log—. Tratar los dos casos igual sería tomar
    /// un antivirus caído por un archivo infectado, o peor, al revés.
    /// </para>
    /// </summary>
    private const string MarcadorFallo = "Failed with hr";

    private readonly OpcionesAntivirus _opciones = opciones.Value;

    public async Task<VeredictoEscaneo> EscanearAsync(string rutaArchivo, CancellationToken ct = default)
    {
        if (ResolverMpCmdRun() is not { } exe)
            return new(ResultadoEscaneo.NoDisponible, "No se encontró MpCmdRun.exe.");

        try
        {
            return await EjecutarAsync(exe, rutaArchivo, ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Fallo al invocar el antivirus sobre {Ruta}.", rutaArchivo);

            return new(ResultadoEscaneo.NoDisponible, ex.Message);
        }
    }

    private async Task<VeredictoEscaneo> EjecutarAsync(string exe, string rutaArchivo, CancellationToken ct)
    {
        // ScanType 3 es el escaneo puntual de una ruta. DisableRemediation hace que Defender
        // informe sin poner el archivo en cuarentena: si lo moviera, el borrado posterior fallaria
        // y no sabriamos si el archivo quedo o no.
        var inicio = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in new[] { "-Scan", "-ScanType", "3", "-File", rutaArchivo, "-DisableRemediation" })
            inicio.ArgumentList.Add(arg);

        using var proceso = Process.Start(inicio)
            ?? throw new InvalidOperationException("No se pudo iniciar MpCmdRun.exe.");

        using var reloj = CancellationTokenSource.CreateLinkedTokenSource(ct);
        reloj.CancelAfter(TimeSpan.FromSeconds(_opciones.TimeoutSegundos));

        var salida = await proceso.StandardOutput.ReadToEndAsync(reloj.Token);

        try
        {
            await proceso.WaitForExitAsync(reloj.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Un escaneo colgado no puede dejar el envío esperando para siempre.
            MatarProceso(proceso);

            return new(ResultadoEscaneo.NoDisponible,
                $"El escaneo superó los {_opciones.TimeoutSegundos} segundos.");
        }

        return Interpretar(proceso.ExitCode, salida, rutaArchivo);
    }

    private VeredictoEscaneo Interpretar(int codigo, string salida, string rutaArchivo)
    {
        if (codigo == 0)
            return new(ResultadoEscaneo.Limpio, null);

        if (salida.Contains(MarcadorFallo, StringComparison.OrdinalIgnoreCase))
        {
            log.LogError(
                "El antivirus no pudo escanear {Ruta}. ¿Está Defender activo en el servidor? Salida: {Salida}",
                rutaArchivo, salida.Trim());

            return new(ResultadoEscaneo.NoDisponible, salida.Trim());
        }

        log.LogWarning("El antivirus rechazó {Ruta}. Salida: {Salida}", rutaArchivo, salida.Trim());

        return new(ResultadoEscaneo.Amenaza, salida.Trim());
    }

    private void MatarProceso(Process proceso)
    {
        try
        {
            if (!proceso.HasExited)
                proceso.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "No se pudo terminar el proceso del antivirus.");
        }
    }

    /// <summary>
    /// La copia de la carpeta de plataforma es la que Defender actualiza; la de Program Files
    /// puede quedar vieja. Por eso se prueba primero la más nueva de aquella.
    /// </summary>
    private string? ResolverMpCmdRun()
    {
        if (!string.IsNullOrWhiteSpace(_opciones.RutaMpCmdRun))
            return File.Exists(_opciones.RutaMpCmdRun) ? _opciones.RutaMpCmdRun : null;

        var plataforma = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Microsoft", "Windows Defender", "Platform");

        if (Directory.Exists(plataforma))
        {
            var candidato = Directory.GetDirectories(plataforma)
                .OrderByDescending(d => d)
                .Select(d => Path.Combine(d, "MpCmdRun.exe"))
                .FirstOrDefault(File.Exists);

            if (candidato is not null)
                return candidato;
        }

        var fijo = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Windows Defender", "MpCmdRun.exe");

        return File.Exists(fijo) ? fijo : null;
    }
}

/// <summary>
/// No escanea nada. Se usa cuando el escaneo está apagado por configuración, y en las pruebas.
/// Avisa al arrancar para que apagarlo no pase inadvertido.
/// </summary>
public sealed class EscanerDesactivado(ILogger<EscanerDesactivado> log) : IEscanerAntivirus
{
    public Task<VeredictoEscaneo> EscanearAsync(string rutaArchivo, CancellationToken ct = default)
    {
        log.LogWarning("El escaneo antivirus está apagado: {Ruta} se guarda sin revisar.", rutaArchivo);

        return Task.FromResult(new VeredictoEscaneo(ResultadoEscaneo.Limpio, "Escaneo desactivado."));
    }
}
