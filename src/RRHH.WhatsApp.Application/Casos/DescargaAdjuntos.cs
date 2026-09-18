using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Excepciones;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Application.Casos;

/// <summary>
/// Cadencia de una vuelta de descarga. La pone quien opera el Worker (appsettings), no la gerencia:
/// es cuanto se insiste contra el proveedor, no una regla sobre el postulante.
/// </summary>
/// <param name="IntentosMaximos">Intentos antes de dar el adjunto por perdido. El id de medio caduca: insistir de mas no sirve.</param>
/// <param name="EsperaBase">Espera tras el primer fallo; se duplica en cada intento.</param>
/// <param name="TiempoMaximoPorArchivo">Una descarga colgada no puede frenar el bucle.</param>
public sealed record ParametrosDescargaAdjuntos(
    int TamanoLote,
    int IntentosMaximos,
    TimeSpan EsperaBase,
    TimeSpan TiempoMaximoPorArchivo);

public sealed record ResumenDescargaAdjuntos(int Intentados, int Descargados, int Rechazados, int Reprogramados)
{
    public override string ToString() =>
        $"{Intentados} intentado(s): {Descargados} descargado(s), {Rechazados} rechazado(s), {Reprogramados} para reintentar.";
}

/// <summary>
/// Baja los archivos que mandaron los postulantes, los escanea y los guarda (V33, ARQ-10).
/// <para>
/// Corre en el Worker y no en el webhook: el proveedor exige responder en segundos, y bajar y escanear
/// un documento no entra en ese tiempo. Por eso el webhook solo registra y esto llega despues, pronto,
/// antes de que el id de medio caduque.
/// </para>
/// <para>
/// Lo que no pasa el antivirus, el tope o las extensiones queda rechazado y no se muestra. Lo que
/// fallo por el proveedor o por el antivirus caido se reintenta con espera creciente, hasta un tope.
/// </para>
/// </summary>
public sealed class DescargaAdjuntos(
    IMensajeService mensajes,
    IWhatsAppProvider proveedor,
    IAlmacenamientoAdjuntos almacenamiento,
    TimeProvider reloj,
    ILogger<DescargaAdjuntos> log)
{
    private enum Desenlace { Descargado, Rechazado, Reprogramado }

    public async Task<ResumenDescargaAdjuntos> ProcesarAsync(
        ParametrosDescargaAdjuntos parametros, CancellationToken ct = default)
    {
        var pendientes = await mensajes.ListarAdjuntosPendientesAsync(
            reloj.GetUtcNow().UtcDateTime, parametros.TamanoLote, ct);

        int descargados = 0, rechazados = 0, reprogramados = 0;

        foreach (var adjunto in pendientes)
        {
            ct.ThrowIfCancellationRequested();

            switch (await ProcesarUnoAsync(adjunto, parametros, ct))
            {
                case Desenlace.Descargado: descargados++; break;
                case Desenlace.Rechazado: rechazados++; break;
                default: reprogramados++; break;
            }
        }

        return new ResumenDescargaAdjuntos(pendientes.Count, descargados, rechazados, reprogramados);
    }

    private async Task<Desenlace> ProcesarUnoAsync(
        MensajeAdjunto adjunto, ParametrosDescargaAdjuntos parametros, CancellationToken ct)
    {
        // El tiempo maximo se mide con el reloj inyectado (ARQ-01): asi se puede probar sin esperar.
        using var limite = new CancellationTokenSource(parametros.TiempoMaximoPorArchivo, reloj);
        using var enlazado = CancellationTokenSource.CreateLinkedTokenSource(ct, limite.Token);

        string? guardadoSinMarcar = null;

        try
        {
            var descarga = await proveedor.DescargarMedioAsync(adjunto.ProveedorMedioId, enlazado.Token);

            if (descarga.Medio is not { } medio)
            {
                return await FallarAsync(
                    adjunto,
                    descarga.Error ?? "El proveedor no entrego el archivo.",
                    reintentable: descarga.Clase == ClaseFallo.Transitorio,
                    parametros,
                    ct);
            }

            ArchivoGuardado guardado;

            await using (medio)
            {
                // El tipo que se usa es el del webhook, que es el que la bandeja muestra.
                guardado = await almacenamiento.GuardarAsync(
                    medio.Contenido, adjunto.NombreArchivo, adjunto.MimeType, enlazado.Token);
            }

            guardadoSinMarcar = guardado.Ruta;

            if (!await mensajes.MarcarAdjuntoDescargadoAsync(adjunto.AdjuntoId, guardado.Ruta, guardado.TamanoBytes, ct))
            {
                // El mensaje se borro mientras bajaba (Regla 17): el archivo no tiene a quien pertenecer.
                log.LogInformation("El adjunto {AdjuntoId} ya no existe: se descarta lo descargado.", adjunto.AdjuntoId);
                return Desenlace.Rechazado;
            }

            guardadoSinMarcar = null;

            log.LogInformation("Adjunto {AdjuntoId} descargado ({Bytes} bytes).", adjunto.AdjuntoId, guardado.TamanoBytes);

            return Desenlace.Descargado;
        }
        catch (ArchivoRechazadoException ex)
        {
            // Antivirus, tope o tipo: repetirlo da lo mismo.
            log.LogWarning("Adjunto {AdjuntoId} rechazado: {Motivo}", adjunto.AdjuntoId, ex.Message);

            return await FallarAsync(adjunto, ex.Message, reintentable: false, parametros, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return await FallarAsync(
                adjunto,
                $"La descarga no termino en {parametros.TiempoMaximoPorArchivo.TotalSeconds:F0} s.",
                reintentable: true,
                parametros,
                ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // El antivirus no respondio, el recurso compartido no esta, algo que nadie esperaba: el
            // archivo puede estar bien, y se vuelve a intentar. Un adjunto no frena al resto del lote.
            log.LogError(ex, "Fallo la descarga del adjunto {AdjuntoId}.", adjunto.AdjuntoId);

            return await FallarAsync(adjunto, ex.Message, reintentable: true, parametros, ct);
        }
        finally
        {
            if (guardadoSinMarcar is not null)
                await BorrarSinFallarAsync(guardadoSinMarcar);
        }
    }

    /// <summary>
    /// Espera base, el doble, el cuadruple... hasta agotar los intentos. Lo que no se va a reintentar
    /// queda rechazado.
    /// </summary>
    private async Task<Desenlace> FallarAsync(
        MensajeAdjunto adjunto, string error, bool reintentable, ParametrosDescargaAdjuntos parametros,
        CancellationToken ct)
    {
        var intento = adjunto.IntentosDescarga + 1;

        DateTime? proximo = reintentable && intento < parametros.IntentosMaximos
            ? reloj.GetUtcNow().UtcDateTime + parametros.EsperaBase * Math.Pow(2, intento - 1)
            : null;

        await mensajes.RegistrarFalloDescargaAsync(adjunto.AdjuntoId, error, proximo, ct);

        return proximo is null ? Desenlace.Rechazado : Desenlace.Reprogramado;
    }

    /// <summary>
    /// Lo guardado que no llego a marcarse no tiene fila que lo referencie: la purga de la Regla 17
    /// nunca lo encontraria. El error que importa es el original, no el de esta limpieza.
    /// </summary>
    private async Task BorrarSinFallarAsync(string ruta)
    {
        try
        {
            await almacenamiento.EliminarAsync(ruta);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "No se pudo borrar el archivo huerfano {Ruta}.", ruta);
        }
    }
}
