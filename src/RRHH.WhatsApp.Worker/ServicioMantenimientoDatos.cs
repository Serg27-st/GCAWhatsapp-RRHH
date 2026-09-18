using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Worker;

/// <summary>
/// Mantenimiento diario de datos. Regla 17: borra los CVs y los archivos llegados por WhatsApp (V33)
/// que cumplieron su plazo y limpia su referencia, alineado a la Ley de Proteccion de Datos
/// Personales. ARQ-13: y saca de la outbox los eventos ya procesados, para que no crezca sin fin.
/// <para>
/// Corre aparte del barrido de reglas y con su propio ritmo, porque no decide nada sobre una
/// conversacion: es mantenimiento de datos, y basta con que pase una vez al dia (Seccion 9.6.5).
/// </para>
/// <para>
/// Su latido sigue llamandose <see cref="ServiciosVigilados.PurgaCv"/> aunque la clase se llame de
/// otra forma: el nombre esta en la tabla y en lo que mira el monitoreo, y renombrarlo daria un bucle
/// «sin latido registrado» hasta que alguien limpie la fila vieja.
/// </para>
/// </summary>
public sealed class ServicioMantenimientoDatos(
    IServiceScopeFactory ambitos,
    IOptions<OpcionesWorker> opciones,
    ILogger<ServicioMantenimientoDatos> log) : BackgroundService
{
    /// <summary>Coincide con la semilla de datos.retencion_cv_dias; solo aplica si falta la clave.</summary>
    private const int RetencionPorDefecto = 365;

    /// <summary>Coincide con la semilla de outbox.retencion_dias_procesados; solo aplica si falta la clave.</summary>
    private const int RetencionOutboxPorDefecto = 30;

    private readonly OpcionesWorker _opciones = opciones.Value;

    /// <summary>Que hizo el ultimo ciclo. Es lo que distingue "vivo" de "vivo y trabajando".</summary>
    private string? _detalle;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        log.LogInformation("Mantenimiento de datos iniciado: cada {Intervalo} hora(s).", _opciones.IntervaloPurgaHoras);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var purgados = await PurgarAsync(ct);

                if (purgados > 0)
                    log.LogInformation("Purga de CVs: {Purgados} archivo(s) eliminados por retencion.", purgados);

                // V33: los archivos que llegaron por WhatsApp son datos de la misma naturaleza que el CV
                // y se purgan en el mismo ciclo, cada uno con su plazo.
                int adjuntos;

                using (var ambito = ambitos.CreateScope())
                {
                    adjuntos = await ambito.ServiceProvider
                        .GetRequiredService<PurgaAdjuntos>()
                        .ProcesarAsync(_opciones.TamanoLotePurga, ct);
                }

                // ARQ-13: el rastro de un evento ya procesado vence. Va en este mismo ciclo porque es
                // mantenimiento igual que la purga de archivos, y a nadie le urge.
                var eventos = await PurgarOutboxAsync(ct);

                _detalle =
                    $"{purgados} CV(s), {adjuntos} adjunto(s) y {eventos} evento(s) purgados en el ultimo ciclo.";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Fallo el mantenimiento de datos. Se reintenta en el proximo ciclo.");
            }

            await Latido.RegistrarAsync(
                ambitos, log, ServiciosVigilados.PurgaCv,
                TimeSpan.FromHours(_opciones.IntervaloPurgaHoras * 3),
                _detalle, ct);

            await EsperaSegura.DormirAsync(TimeSpan.FromHours(_opciones.IntervaloPurgaHoras), ct);
        }

        log.LogInformation("Mantenimiento de datos detenido.");
    }

    /// <summary>
    /// ARQ-13: los eventos procesados mas viejos que <c>outbox.retencion_dias_procesados</c>. El lote de
    /// la purga tambien acota cada sentencia de borrado.
    /// </summary>
    private async Task<int> PurgarOutboxAsync(CancellationToken ct)
    {
        using var ambito = ambitos.CreateScope();
        var sp = ambito.ServiceProvider;

        var configuracion = await sp.GetRequiredService<IConfiguracionReglasService>().ObtenerTodasAsync(ct);

        var dias = configuracion.TryGetValue(ClavesConfiguracion.OutboxRetencionDiasProcesados, out var valor)
            && int.TryParse(valor, out var n) && n > 0
                ? n
                : RetencionOutboxPorDefecto;

        return await sp.GetRequiredService<IEventoSistemaService>()
            .PurgarProcesadosAsync(dias, _opciones.TamanoLotePurga, ct);
    }

    private async Task<int> PurgarAsync(CancellationToken ct)
    {
        IReadOnlyList<int> respuestas;

        using (var ambito = ambitos.CreateScope())
        {
            var sp = ambito.ServiceProvider;

            var configuracion = await sp.GetRequiredService<IConfiguracionReglasService>()
                .ObtenerTodasAsync(ct);

            var dias = configuracion.TryGetValue(ClavesConfiguracion.RetencionCvDias, out var valor)
                && int.TryParse(valor, out var n) && n > 0
                    ? n
                    : RetencionPorDefecto;

            var vencidas = await sp.GetRequiredService<IJobFormsService>()
                .ListarCvsPorPurgarAsync(dias, _opciones.TamanoLotePurga, ct);

            respuestas = [.. vencidas.Select(r => r.RespuestaId)];
        }

        var purgados = 0;

        foreach (var respuestaId in respuestas)
        {
            if (ct.IsCancellationRequested)
                break;

            using var ambito = ambitos.CreateScope();

            try
            {
                await ambito.ServiceProvider
                    .GetRequiredService<IJobFormsService>()
                    .PurgarCvAsync(respuestaId, ct);

                purgados++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Un archivo que no se puede borrar —permisos, recurso compartido caido— no debe
                // frenar al resto: vuelve a entrar solo en el proximo ciclo.
                log.LogError(ex, "Fallo la purga del CV de la respuesta {RespuestaId}.", respuestaId);
            }
        }

        return purgados;
    }
}
