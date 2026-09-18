using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Infrastructure.Almacenamiento;

/// <summary>
/// El circuito por el que entra al recurso compartido cualquier archivo de un postulante: cuarentena,
/// antivirus, tope de tamaño y extensiones permitidas (Seccion 9.6.1). Lo comparten el CV del
/// formulario y los adjuntos de WhatsApp (V33): dos copias del mismo control terminan diciendo cosas
/// distintas.
/// <para>
/// El archivo se escribe en disco y no en SQL Server (Seccion 8.3). En el despliegue on-premise la
/// carpeta es un recurso compartido de la empresa.
/// </para>
/// </summary>
public abstract class AlmacenamientoArchivosLocal(
    AlmacenamientoArchivosLocal.Politica politica,
    IEscanerAntivirus antivirus,
    TimeProvider reloj,
    ILogger log)
{
    /// <summary>Tamano de bloque al copiar. El tope se controla contando lo copiado, no de una vez.</summary>
    private const int Bloque = 64 * 1024;

    /// <summary>
    /// Donde aterriza el archivo mientras se lo revisa. Va dentro de la carpeta configurada para que
    /// el File.Move sea un renombrado en el mismo volumen y no una copia, pero se excluye de los
    /// respaldos: lo que hay aca todavia no paso el antivirus.
    /// </summary>
    public const string CarpetaCuarentena = "_cuarentena";

    /// <param name="Nombre">Como se llama el archivo en los mensajes: «CV», «adjunto».</param>
    /// <param name="ExigirEscaneo">Sin antivirus disponible, rechazar (true) o guardar avisando (false).</param>
    public sealed record Politica(
        string Carpeta,
        int TamanoMaximoMb,
        IReadOnlyCollection<string> ExtensionesPermitidas,
        bool ExigirEscaneo,
        string Nombre);

    /// <summary>
    /// Lo que se lanza cuando el archivo no pasa las reglas. El CV conserva su
    /// <see cref="InvalidOperationException"/> de siempre; los adjuntos necesitan distinguir el rechazo
    /// en firme de un fallo que se puede reintentar.
    /// </summary>
    protected virtual Exception Rechazo(string motivo) => new InvalidOperationException(motivo);

    /// <param name="extension">En minuscula y con punto. Nula si no se pudo determinar.</param>
    protected async Task<ArchivoGuardado> GuardarArchivoAsync(Stream contenido, string? extension, CancellationToken ct)
    {
        if (extension is null || !politica.ExtensionesPermitidas.Contains(extension))
        {
            throw Rechazo(extension is null
                ? $"No se reconoce el tipo del archivo: no se puede guardar un {politica.Nombre} asi."
                : $"La extension '{extension}' no esta permitida para un {politica.Nombre}.");
        }

        // Se reparte por mes para que la carpeta no termine con decenas de miles de archivos
        // sueltos, que es lo que vuelve lenta la purga de la Regla 17.
        var relativa = Path.Combine(
            reloj.GetUtcNow().UtcDateTime.ToString("yyyy-MM"),
            $"{Guid.NewGuid():N}{extension}");

        var absoluta = Path.Combine(politica.Carpeta, relativa);

        // El archivo aterriza primero en cuarentena, fuera de lo que ven los analistas. Escribirlo
        // directo en su destino y escanearlo despues dejaria una ventana —corta, pero real— con un
        // archivo sin revisar a su alcance.
        var cuarentena = Path.Combine(politica.Carpeta, CarpetaCuarentena, $"{Guid.NewGuid():N}{extension}");

        Directory.CreateDirectory(Path.GetDirectoryName(cuarentena)!);

        long copiados;

        try
        {
            copiados = await CopiarConTopeAsync(contenido, cuarentena, ct);
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

        log.LogInformation("Archivo ({Tipo}) almacenado en {Ruta}.", politica.Nombre, relativa);

        // Se devuelve la ruta relativa: mover el recurso compartido no debe invalidar lo guardado.
        return new ArchivoGuardado(relativa, copiados);
    }

    /// <summary>
    /// Seccion 9.6.1: el archivo pasa por el antivirus antes de quedar guardado. Es el unico camino
    /// por el que se escribe, asi que ninguna ruta lo puede saltear.
    /// </summary>
    private async Task RevisarAsync(string cuarentena, CancellationToken ct)
    {
        var veredicto = await antivirus.EscanearAsync(cuarentena, ct);

        switch (veredicto.Resultado)
        {
            case ResultadoEscaneo.Limpio:
                return;

            case ResultadoEscaneo.Amenaza:
                throw Rechazo("El archivo fue rechazado por el antivirus.");

            default:
                // No se pudo escanear. Guardarlo igual seria justamente el caso que el requisito
                // quiere evitar. No es un rechazo: el archivo puede estar bien, y se puede volver a
                // intentar cuando el antivirus responda.
                if (politica.ExigirEscaneo)
                {
                    throw new InvalidOperationException(
                        "No se pudo revisar el archivo con el antivirus. Intente de nuevo mas tarde.");
                }

                log.LogWarning(
                    "Se guarda un archivo ({Tipo}) sin escanear porque ExigirEscaneo esta en falso: {Detalle}",
                    politica.Nombre, veredicto.Detalle);

                return;
        }
    }

    /// <summary>
    /// Copia contando los bytes y corta al pasarse del tope (Seccion 9.6.1).
    /// <para>
    /// Se cuenta al copiar en vez de mirar <c>Stream.Length</c> porque el origen puede no ser medible
    /// —un cuerpo HTTP en streaming no lo es— y porque un largo declarado por quien manda el archivo no
    /// es una comprobacion, es una promesa.
    /// </para>
    /// </summary>
    private async Task<long> CopiarConTopeAsync(Stream origen, string destino, CancellationToken ct)
    {
        var tope = (long)politica.TamanoMaximoMb * 1024 * 1024;
        var copiados = 0L;

        var buffer = new byte[Bloque];

        await using var archivo = File.Create(destino);

        int leidos;

        while ((leidos = await origen.ReadAsync(buffer, ct)) > 0)
        {
            copiados += leidos;

            if (copiados > tope)
            {
                throw Rechazo(
                    $"El {politica.Nombre} supera el maximo de {politica.TamanoMaximoMb} MB permitido.");
            }

            await archivo.WriteAsync(buffer.AsMemory(0, leidos), ct);
        }

        return copiados;
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
            // No se propaga: lo que importa es el error original, no el de la limpieza.
            log.LogWarning(ex, "No se pudo borrar el archivo incompleto {Ruta}.", absoluta);
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
            log.LogInformation("Archivo ({Tipo}) eliminado: {Ruta}.", politica.Nombre, ruta);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Devuelve nulo si la ruta se sale de la carpeta configurada. La ruta llega desde la base y no del
    /// usuario, pero un valor manipulado no puede terminar leyendo ni borrando archivos fuera del
    /// recurso compartido.
    /// <para>
    /// La raiz se compara con su separador al final: sin el, <c>cv-viejo\x</c> pasaba por estar dentro
    /// de <c>cv</c>, porque empieza con las mismas letras.
    /// </para>
    /// </summary>
    private string? Resolver(string ruta)
    {
        var raiz = Path.GetFullPath(politica.Carpeta);

        if (!Path.EndsInDirectorySeparator(raiz))
            raiz += Path.DirectorySeparatorChar;

        var completa = Path.GetFullPath(Path.Combine(raiz, ruta));

        if (completa.StartsWith(raiz, StringComparison.OrdinalIgnoreCase))
            return completa;

        log.LogWarning("Se rechazo una ruta ({Tipo}) fuera de la carpeta configurada: {Ruta}", politica.Nombre, ruta);

        return null;
    }
}
