using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Worker;

/// <summary>
/// Vigila el paso del tiempo, que es lo que dispara las reglas que no responden a un mensaje:
/// el escalamiento a las 2 horas (Regla 2), el recordatorio del JobForms a las 24 y el aviso al
/// analista a las 48 (Regla 9), y el archivado del caso (Regla 16).
/// <para>
/// Las consultas solo prefiltran candidatos baratos. Cuanto tiempo paso de verdad, si ese tiempo
/// cuenta fuera del horario laboral y si el caso merece archivarse lo deciden las reglas, que son
/// las que conocen los parametros.
/// </para>
/// </summary>
public sealed class ServicioBarridoTiempo(
    IServiceScopeFactory ambitos,
    IOptions<OpcionesWorker> opciones,
    TimeProvider reloj,
    ILogger<ServicioBarridoTiempo> log) : BackgroundService
{
    private readonly OpcionesWorker _opciones = opciones.Value;

    /// <summary>Que hizo el ultimo ciclo. Es lo que distingue "vivo" de "vivo y trabajando".</summary>
    private string? _detalle;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        log.LogInformation(
            "Barrido por tiempo iniciado: cada {Intervalo} minuto(s), hasta {Lote} hilos por vuelta.",
            _opciones.IntervaloBarridoMinutos, _opciones.TamanoLoteBarrido);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var acciones = await BarrerAsync(ct);

                _detalle = $"{acciones} accion(es) en el ultimo barrido.";

                if (acciones > 0)
                    log.LogInformation("Barrido por tiempo: {Acciones} accion(es) ejecutadas.", acciones);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Fallo el barrido por tiempo. Se reintenta en el proximo ciclo.");
            }

            await Latido.RegistrarAsync(
                ambitos, log, ServiciosVigilados.BarridoTiempo,
                TimeSpan.FromMinutes(_opciones.IntervaloBarridoMinutos * 3),
                _detalle, ct);

            await EsperaSegura.DormirAsync(TimeSpan.FromMinutes(_opciones.IntervaloBarridoMinutos), ct);
        }

        log.LogInformation("Barrido por tiempo detenido.");
    }

    private async Task<int> BarrerAsync(CancellationToken ct)
    {
        var total = 0;

        foreach (var conversacionId in await ReunirCandidatasAsync(ct))
        {
            if (ct.IsCancellationRequested)
                break;

            using var ambito = ambitos.CreateScope();

            try
            {
                total += await ambito.ServiceProvider
                    .GetRequiredService<BarridoTiempo>()
                    .ProcesarConversacionAsync(conversacionId, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Una conversacion que falla no detiene el barrido: el resto se sigue evaluando y
                // esta vuelve a entrar sola en el proximo ciclo.
                log.LogError(ex, "Fallo el barrido de la conversacion {ConversacionId}.", conversacionId);
            }
        }

        // FUN-12 (A10): los titulares que volvieron de una ausencia reciben su resumen. Va en su propio
        // ambito: no es una regla sobre un hilo, y si falla no tiene por que frenar el resto.
        try
        {
            using var ambitoAviso = ambitos.CreateScope();

            await ambitoAviso.ServiceProvider.GetRequiredService<AvisoRetornoAusencia>().ProcesarAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogError(ex, "Fallo el aviso de retorno de ausencias.");
        }

        // FUN-10, FUN-11: lo que vence por postulacion va aparte. Una persona puede tener un proceso
        // archivandose en una cuenta y otro vivo en otra, y cada uno se evalua con su propio contexto.
        foreach (var postulacionId in await ReunirPostulacionesAsync(ct))
        {
            if (ct.IsCancellationRequested)
                break;

            using var ambito = ambitos.CreateScope();

            try
            {
                total += await ambito.ServiceProvider
                    .GetRequiredService<BarridoTiempo>()
                    .ProcesarPostulacionAsync(postulacionId, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                log.LogError(ex, "Fallo el barrido de la postulacion {PostulacionId}.", postulacionId);
            }
        }

        return total;
    }

    /// <summary>
    /// FUN-10 y FUN-11: postulaciones con un cierre de cortesia pedido, en la ventana de aviso previo
    /// al archivado, o ya vencidas. Sin repetir: la misma puede estar en mas de una lista.
    /// </summary>
    private async Task<IReadOnlyCollection<int>> ReunirPostulacionesAsync(CancellationToken ct)
    {
        using var ambito = ambitos.CreateScope();
        var sp = ambito.ServiceProvider;

        var postulaciones = sp.GetRequiredService<IPostulacionService>();
        var configuracion = await sp.GetRequiredService<IConfiguracionReglasService>().ObtenerTodasAsync(ct);

        var lote = _opciones.TamanoLoteBarrido;
        var dias = Entero(configuracion, ClavesConfiguracion.ArchivadoDias, 90);
        var diasAviso = Entero(configuracion, ClavesConfiguracion.ArchivadoAvisoDias, 7);

        var candidatas = new HashSet<int>(await postulaciones.ListarCierresPendientesAsync(lote, ct));

        foreach (var id in await postulaciones.ListarPorAvisarArchivadoAsync(dias, diasAviso, lote, ct))
            candidatas.Add(id);

        foreach (var id in await postulaciones.ListarPorArchivarAsync(dias, lote, ct))
            candidatas.Add(id);

        return candidatas;
    }

    /// <summary>
    /// Junta los hilos que alguna regla por tiempo podria tocar, sin repetirlos: el mismo hilo
    /// puede ser candidato a escalar y a archivar a la vez, y una sola evaluacion cubre ambas
    /// porque las tres reglas comparten el disparador.
    /// </summary>
    private async Task<IReadOnlyCollection<int>> ReunirCandidatasAsync(CancellationToken ct)
    {
        using var ambito = ambitos.CreateScope();
        var sp = ambito.ServiceProvider;

        var conversaciones = sp.GetRequiredService<IConversacionService>();
        var invitaciones = sp.GetRequiredService<IJobFormsInvitacionService>();

        var configuracion = await sp.GetRequiredService<IConfiguracionReglasService>()
            .ObtenerTodasAsync(ct);

        var lote = _opciones.TamanoLoteBarrido;
        var candidatas = new HashSet<int>();

        foreach (var id in await conversaciones.ListarPendientesEscalamientoAsync(lote, ct))
            candidatas.Add(id);

        // FUN-05: las ya escaladas que siguen esperando son candidatas al aviso a Jefatura.
        foreach (var id in await conversaciones.ListarPendientesSegundoNivelAsync(lote, ct))
            candidatas.Add(id);

        // FUN-06: las que quedaron en silencio en el menu del bot, y las que esperan en la bandeja
        // general sin que nadie las tome.
        foreach (var id in await conversaciones.ListarPendientesDerivacionMenuAsync(lote, ct))
            candidatas.Add(id);

        foreach (var id in await conversaciones.ListarPendientesAvisoClasificacionAsync(lote, ct))
            candidatas.Add(id);

        // FUN-07 (A1): las transferencias no urgentes cuyo plazo ya vencio.
        var ahora = reloj.GetUtcNow().UtcDateTime;

        foreach (var id in await conversaciones.ListarConversacionesConTransferenciaVencidaAsync(ahora, lote, ct))
            candidatas.Add(id);

        var diasArchivado = Entero(configuracion, ClavesConfiguracion.ArchivadoDias, 90);

        foreach (var id in await conversaciones.ListarPendientesArchivadoAsync(diasArchivado, lote, ct))
            candidatas.Add(id);

        // El prefiltro usa los mismos plazos que la regla, leidos del mismo lugar, para no traer
        // invitaciones que la Regla 9 va a descartar por prematuras.
        var recordatorio = TimeSpan.FromHours(
            Entero(configuracion, ClavesConfiguracion.RecordatorioJobFormsHoras, 24));

        var aviso = TimeSpan.FromHours(
            Entero(configuracion, ClavesConfiguracion.AvisoAnalistaJobFormsHoras, 48));

        foreach (var invitacion in (await invitaciones.ListarPendientesRecordatorioAsync(recordatorio, ct)).Take(lote))
            candidatas.Add(invitacion.ConversacionId);

        foreach (var invitacion in (await invitaciones.ListarPendientesAvisoAnalistaAsync(aviso, ct)).Take(lote))
            candidatas.Add(invitacion.ConversacionId);

        return candidatas;
    }

    private static int Entero(IReadOnlyDictionary<string, string> configuracion, string clave, int porDefecto) =>
        configuracion.TryGetValue(clave, out var valor) && int.TryParse(valor, out var n) && n > 0
            ? n
            : porDefecto;
}
