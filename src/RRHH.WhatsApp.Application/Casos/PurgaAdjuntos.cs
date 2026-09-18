using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Application.Casos;

/// <summary>
/// Regla 17 para los archivos que mandan los postulantes por WhatsApp (V33, FUN-14): son datos
/// personales de la misma naturaleza que el CV, y se borran con su propio plazo,
/// <c>datos.retencion_adjuntos_dias</c>, contado como el del CV (A5).
/// <para>
/// Primero se borra el archivo y despues se marca. Al reves, una falla en el medio dejaria un archivo
/// sin fila que lo referencie, que ninguna purga volveria a encontrar; asi, lo peor es que la proxima
/// vuelta intente borrar algo que ya no esta.
/// </para>
/// </summary>
public sealed class PurgaAdjuntos(
    IMensajeService mensajes,
    IAlmacenamientoAdjuntos almacenamiento,
    IConfiguracionReglasService configuracion,
    ILogger<PurgaAdjuntos> log)
{
    /// <summary>Coincide con la semilla de datos.retencion_adjuntos_dias; solo aplica si falta la clave.</summary>
    private const int RetencionPorDefecto = 365;

    /// <summary>Devuelve cuantos adjuntos se purgaron.</summary>
    public async Task<int> ProcesarAsync(int maximo, CancellationToken ct = default)
    {
        var parametros = await configuracion.ObtenerTodasAsync(ct);

        var dias = parametros.TryGetValue(ClavesConfiguracion.RetencionAdjuntosDias, out var valor)
            && int.TryParse(valor, out var n) && n > 0
                ? n
                : RetencionPorDefecto;

        var vencidos = await mensajes.ListarAdjuntosPorPurgarAsync(dias, maximo, ct);
        var purgados = 0;

        foreach (var adjunto in vencidos)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (adjunto.Ruta is { } ruta)
                    await almacenamiento.EliminarAsync(ruta, ct);

                await mensajes.MarcarAdjuntoPurgadoAsync(adjunto.AdjuntoId, ct);
                purgados++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Un archivo que no se puede borrar —permisos, recurso compartido caido— no debe frenar
                // al resto: sigue descargado y vuelve a entrar en la proxima vuelta.
                log.LogError(ex, "Fallo la purga del adjunto {AdjuntoId}.", adjunto.AdjuntoId);
            }
        }

        if (purgados > 0)
            log.LogInformation("Purga de adjuntos: {Purgados} archivo(s) eliminados por retencion.", purgados);

        return purgados;
    }
}
