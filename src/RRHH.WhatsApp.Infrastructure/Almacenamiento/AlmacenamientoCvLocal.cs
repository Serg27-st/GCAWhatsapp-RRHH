using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Infrastructure.Almacenamiento;

/// <summary>
/// Guarda los CVs en el sistema de archivos, que en el despliegue on-premise es un recurso
/// compartido de la empresa. La interfaz deja la puerta abierta a mover los archivos a
/// almacenamiento de objetos sin tocar el resto del sistema (Seccion 8.3).
/// </summary>
public sealed class AlmacenamientoCvLocal(
    IOptions<OpcionesCv> opciones,
    IEscanerAntivirus antivirus,
    TimeProvider reloj,
    ILogger<AlmacenamientoCvLocal> log) : IAlmacenamientoCv
{
    /// <summary>Tamano de bloque al copiar. El tope se controla contando lo copiado, no de una vez.</summary>
    private const int Bloque = 64 * 1024;

    /// <summary>
    /// Donde aterriza el archivo mientras se lo revisa. Va dentro de la carpeta de CVs para que
    /// el File.Move sea un renombrado en el mismo volumen y no una copia, pero se excluye de la
    /// verificacion de respaldos: lo que hay aca no es un CV todavia.
    /// </summary>
    public const string CarpetaCuarentena = "_cuarentena";

    private readonly OpcionesCv _opciones = opciones.Value;

    public async Task<string> GuardarAsync(
        Stream contenido, string nombreArchivo, string contentType, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(nombreArchivo).ToLowerInvariant();

        if (!_opciones.ExtensionesPermitidas.Contains(extension))
        {
            throw new InvalidOperationException(
                $"La extension '{extension}' no esta permitida para un CV.");
        }

        // Se reparte por mes para que la carpeta no termine con decenas de miles de archivos
        // sueltos, que es lo que vuelve lenta la purga de la Regla 17.
        var relativa = Path.Combine(
            reloj.GetUtcNow().UtcDateTime.ToString("yyyy-MM"),
            $"{Guid.NewGuid():N}{extension}");

        var absoluta = Path.Combine(_opciones.Carpeta, relativa);

        // El archivo aterriza primero en cuarentena, fuera de la carpeta compartida. Escribirlo
        // directo en su destino y escanearlo despues dejaria una ventana —corta, pero real— con
        // un archivo sin revisar al alcance de los analistas.
        var cuarentena = Path.Combine(
            _opciones.Carpeta, CarpetaCuarentena, $"{Guid.NewGuid():N}{extension}");

        Directory.CreateDirectory(Path.GetDirectoryName(cuarentena)!);

        try
        {
            await CopiarConTopeAsync(contenido, cuarentena, ct);
            await RevisarAsync(cuarentena, ct);

            Directory.CreateDirectory(Path.GetDirectoryName(absoluta)!);
            File.Move(cuarentena, absoluta, overwrite: true);
        }
        catch
        {
            // Un archivo a medio escribir, o uno rechazado, es peor que ninguno: la purga de la
            // Regla 17 lo encontraria sin fila que lo referencie y nadie sabria de donde salio.
            Borrar(cuarentena);
            throw;
        }

        log.LogInformation("CV almacenado en {Ruta}.", relativa);

        // Se devuelve la ruta relativa: mover el recurso compartido no debe invalidar lo guardado.
        return relativa;
    }

    /// <summary>
    /// Seccion 9.6.1: el adjunto pasa por el antivirus antes de quedar guardado. Es el unico
    /// camino por el que se escribe un CV, asi que ninguna ruta lo puede saltear.
    /// </summary>
    private async Task RevisarAsync(string cuarentena, CancellationToken ct)
    {
        var veredicto = await antivirus.EscanearAsync(cuarentena, ct);

        switch (veredicto.Resultado)
        {
            case ResultadoEscaneo.Limpio:
                return;

            case ResultadoEscaneo.Amenaza:
                throw new InvalidOperationException(
                    "El archivo fue rechazado por el antivirus.");

            default:
                // No se pudo escanear. Guardarlo igual seria justamente el caso que el requisito
                // quiere evitar; y un CV rechazado se puede volver a subir.
                if (_opciones.Antivirus.ExigirEscaneo)
                {
                    throw new InvalidOperationException(
                        "No se pudo revisar el archivo con el antivirus. Intente de nuevo mas tarde.");
                }

                log.LogWarning(
                    "Se guarda un CV sin escanear porque ExigirEscaneo esta en falso: {Detalle}",
                    veredicto.Detalle);

                return;
        }
    }

    /// <summary>
    /// Copia contando los bytes y corta al pasarse del tope (Seccion 9.6.1).
    /// <para>
    /// Se cuenta al copiar en vez de mirar <c>Stream.Length</c> porque el origen puede no ser
    /// medible —un cuerpo HTTP en streaming no lo es— y porque un largo declarado por quien sube
    /// el archivo no es una comprobacion, es una promesa.
    /// </para>
    /// </summary>
    private async Task CopiarConTopeAsync(Stream origen, string destino, CancellationToken ct)
    {
        var tope = (long)_opciones.TamanoMaximoMb * 1024 * 1024;
        var copiados = 0L;

        var buffer = new byte[Bloque];

        await using var archivo = File.Create(destino);

        int leidos;

        while ((leidos = await origen.ReadAsync(buffer, ct)) > 0)
        {
            copiados += leidos;

            if (copiados > tope)
            {
                throw new InvalidOperationException(
                    $"El CV supera el maximo de {_opciones.TamanoMaximoMb} MB permitido.");
            }

            await archivo.WriteAsync(buffer.AsMemory(0, leidos), ct);
        }
    }

    private void Borrar(string absoluta)
    {
        try
        {
            if (File.Exists(absoluta))
                File.Delete(absoluta);
        }
        catch (Exception ex)
        {
            // No se propaga: lo que importa es el error original del envio, no el de la limpieza.
            log.LogWarning(ex, "No se pudo borrar el CV incompleto {Ruta}.", absoluta);
        }
    }

    public Task<Stream?> ObtenerAsync(string ruta, CancellationToken ct = default)
    {
        var absoluta = Resolver(ruta);

        if (absoluta is null || !File.Exists(absoluta))
            return Task.FromResult<Stream?>(null);

        return Task.FromResult<Stream?>(File.OpenRead(absoluta));
    }

    public Task EliminarAsync(string ruta, CancellationToken ct = default)
    {
        var absoluta = Resolver(ruta);

        if (absoluta is null)
            return Task.CompletedTask;

        if (File.Exists(absoluta))
        {
            File.Delete(absoluta);
            log.LogInformation("CV eliminado: {Ruta}.", ruta);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Devuelve nulo si la ruta se sale de la carpeta configurada. La ruta llega desde la base y
    /// no del usuario, pero un valor manipulado no puede terminar leyendo ni borrando archivos
    /// fuera del recurso compartido.
    /// </summary>
    private string? Resolver(string ruta)
    {
        var raiz = Path.GetFullPath(_opciones.Carpeta);
        var completa = Path.GetFullPath(Path.Combine(raiz, ruta));

        if (completa.StartsWith(raiz, StringComparison.OrdinalIgnoreCase))
            return completa;

        log.LogWarning("Se rechazo una ruta de CV fuera de la carpeta configurada: {Ruta}", ruta);

        return null;
    }
}
