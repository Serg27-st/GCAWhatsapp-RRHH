using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using RRHH.WhatsApp.Api.Seguridad;
using RRHH.WhatsApp.Contracts.TiempoReal;

namespace RRHH.WhatsApp.Api.TiempoReal;

/// <summary>
/// Canal en vivo hacia la bandeja (Sección 9.6.3). Vive en la Api y no en el Frontend porque el
/// Frontend solo habla HTTP con la Api: mover el hub allá lo obligaría a leer la outbox por su
/// cuenta, que es justo el límite que el diseño evita.
/// <para>
/// Cada analista tiene su propio grupo, y el grupo sale del token — no de lo que pida el cliente.
/// Mientras el id viajaba como parámetro, cualquiera podía escuchar los avisos de otro y la
/// Regla 4 quedaba rota también acá.
/// </para>
/// </summary>
[Authorize]
public sealed class HubBandeja(ILogger<HubBandeja> log) : Hub
{
    /// <summary>
    /// Suscribe la conexión a los avisos del analista autenticado. No recibe parámetros a
    /// propósito: no hay nada que el cliente pueda elegir acá.
    /// </summary>
    public async Task Suscribir()
    {
        var analistaId = Context.User!.AnalistaId();

        await Groups.AddToGroupAsync(Context.ConnectionId, Grupo(analistaId));

        log.LogDebug("Conexión {Conexion} suscrita al analista {AnalistaId}.",
            Context.ConnectionId, analistaId);
    }

    public static string Grupo(int analistaId) => $"analista-{analistaId}";
}

/// <summary>Envía avisos al hub sin que quien los produce dependa de SignalR.</summary>
public interface IAvisoBandeja
{
    Task EnviarAsync(NotificacionAnalista aviso, CancellationToken ct = default);
}

public sealed class AvisoBandeja(IHubContext<HubBandeja> hub) : IAvisoBandeja
{
    public Task EnviarAsync(NotificacionAnalista aviso, CancellationToken ct = default) =>
        hub.Clients
            .Group(HubBandeja.Grupo(aviso.AnalistaId))
            .SendAsync(CanalBandeja.RecibirNotificacion, aviso, ct);
}
