using System.Text.Json;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Contracts.TiempoReal;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Api.TiempoReal;

/// <summary>
/// Saca de la outbox los avisos para analistas y los empuja al hub (Sección 9.6.3).
/// <para>
/// El evento <c>AnalistaNotificado</c> se venía publicando desde el primer día y no lo consumía
/// nadie: el aviso de 48h por formulario sin completar (Regla 9), el de multi-cuenta (Regla 6) y
/// el de escalamiento (Regla 2) se calculaban bien y no llegaban a ninguna persona. Este bucle es
/// lo que cierra ese circuito.
/// </para>
/// <para>
/// Vive en la Api y no en el Worker porque el hub es de la Api: un proceso aparte no puede empujar
/// a conexiones que no tiene. Consume tipos distintos de los que atiende el Worker, así que ambos
/// leen la misma tabla sin pisarse nunca la misma fila.
/// </para>
/// </summary>
public sealed class DifusorNotificaciones(
    IServiceScopeFactory ambitos,
    IAvisoBandeja avisos,
    ILogger<DifusorNotificaciones> log) : BackgroundService
{
    /// <summary>Los únicos tipos que este bucle pide de la cola.</summary>
    public static readonly IReadOnlyCollection<string> TiposQueAtiende =
        [TiposEvento.AnalistaNotificado];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const int TamanoLote = 100;
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PausaTrasError = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        log.LogInformation("Difusor de notificaciones iniciado: cada {Intervalo}s.", Intervalo.TotalSeconds);

        while (!ct.IsCancellationRequested)
        {
            var espera = Intervalo;

            try
            {
                var leidos = await DifundirLoteAsync(ct);

                // Lote lleno significa cola acumulada: se sigue de inmediato en vez de dormir.
                if (leidos >= TamanoLote)
                    continue;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Fallo el lote de notificaciones. Se reintenta en {Pausa}s.",
                    PausaTrasError.TotalSeconds);

                espera = PausaTrasError;
            }

            await DormirAsync(espera, ct);
        }

        log.LogInformation("Difusor de notificaciones detenido.");
    }

    private async Task<int> DifundirLoteAsync(CancellationToken ct)
    {
        using var ambito = ambitos.CreateScope();

        var eventos = ambito.ServiceProvider.GetRequiredService<IEventoSistemaService>();

        var pendientes = await eventos.ObtenerPendientesAsync(TamanoLote, TiposQueAtiende, ct);

        foreach (var evento in pendientes)
        {
            if (ct.IsCancellationRequested)
                break;

            await DifundirUnoAsync(evento, eventos, ct);
        }

        return pendientes.Count;
    }

    private async Task DifundirUnoAsync(
        EventoSistema evento, IEventoSistemaService eventos, CancellationToken ct)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<PayloadAviso>(evento.Payload, Json)
                ?? throw new InvalidOperationException("Payload ilegible.");

            if (payload.AnalistaId <= 0)
                throw new InvalidOperationException($"Sin analista destino: {evento.Payload}");

            await avisos.EnviarAsync(new NotificacionAnalista(
                payload.AnalistaId,
                payload.ConversacionId,
                payload.Mensaje ?? "Tenés una novedad en tu bandeja.",
                evento.FechaCreacion), ct);

            // Se marca procesado aunque el analista no esté conectado. El aviso no se pierde: la
            // conversación sigue en su bandeja y la ve al entrar. Guardarlo para reintentar
            // convertiría la cola en una bandeja de notificaciones, que no es lo que es.
            await eventos.MarcarProcesadoAsync(evento.EventoId, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogError(ex, "No se pudo difundir el evento {EventoId}.", evento.EventoId);

            await eventos.MarcarFallidoAsync(evento.EventoId, ex.Message, reintentosMaximos: 3, ct);
        }
    }

    /// <summary>Absorbe la cancelación para que apagar la Api no deje un error en el log.</summary>
    private static async Task DormirAsync(TimeSpan espera, CancellationToken ct)
    {
        try
        {
            await Task.Delay(espera, ct);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed record PayloadAviso(int AnalistaId, string? Mensaje, int? ConversacionId);
}
