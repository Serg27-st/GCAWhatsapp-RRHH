using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Contracts.TiempoReal;

namespace RRHH.WhatsApp.Frontend.Servicios;

/// <summary>
/// Conexión al hub de la Api (Sección 9.6.3). Es lo que hace que un escalamiento, una
/// transferencia o un aviso de formulario sin completar aparezcan sin refrescar.
/// <para>
/// Se conecta por HTTP como cualquier otro cliente: el Frontend sigue sin conocer la base ni el
/// dominio. Uno por circuito de Blazor, así cada analista escucha lo suyo.
/// </para>
/// </summary>
public sealed class CanalEnVivo(IOptions<OpcionesApi> opciones, ILogger<CanalEnVivo> log) : IAsyncDisposable
{
    private readonly OpcionesApi _opciones = opciones.Value;

    private HubConnection? _conexion;

    /// <summary>Se dispara al llegar un aviso, en el hilo del circuito.</summary>
    public event Func<NotificacionAnalista, Task>? Aviso;

    public bool Conectado => _conexion?.State == HubConnectionState.Connected;

    /// <summary>
    /// Conecta y se suscribe a lo de un analista. Si el hub no está disponible no lanza: la
    /// bandeja sigue andando con su refresco por intervalo, que es el respaldo de este canal.
    /// </summary>
    public async Task ConectarAsync(string token)
    {
        await DesconectarAsync();

        var url = _opciones.BaseUrl.TrimEnd('/') + CanalBandeja.Ruta;

        _conexion = new HubConnectionBuilder()
            .WithUrl(url, opciones => opciones.AccessTokenProvider = () => Task.FromResult<string?>(token))
            .WithAutomaticReconnect()
            .Build();

        _conexion.On<NotificacionAnalista>(CanalBandeja.RecibirNotificacion, async aviso =>
        {
            if (Aviso is { } manejador)
                await manejador(aviso);
        });

        // Tras una reconexión hay que volver a pedir el grupo: el servidor no recuerda a qué
        // estaba suscrita una conexión que se cayó.
        _conexion.Reconnected += async _ =>
        {
            await SuscribirAsync();
        };

        try
        {
            await _conexion.StartAsync();
            await SuscribirAsync();

            log.LogInformation("Canal en vivo conectado.");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "No se pudo abrir el canal en vivo. La bandeja usa el refresco por intervalo.");
        }
    }

    private Task SuscribirAsync() =>
        _conexion is null ? Task.CompletedTask : _conexion.InvokeAsync(CanalBandeja.Suscribir);

    private async Task DesconectarAsync()
    {
        if (_conexion is null)
            return;

        await _conexion.DisposeAsync();
        _conexion = null;
    }

    public async ValueTask DisposeAsync() => await DesconectarAsync();
}
