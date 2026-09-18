using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// La base o la red se caen justo al publicar el evento, después de que el caso de uso ya guardó lo
/// suyo. Es el punto exacto donde, sin unidad de trabajo (V28), quedaba un estado a medias (C5, C6).
/// </summary>
internal sealed class EventosQueFallanAlPublicar(IEventoSistemaService interno) : IEventoSistemaService
{
    public Task PublicarAsync(string tipo, object payload, Guid correlationId, CancellationToken ct = default) =>
        throw new InvalidOperationException("Se cayó la conexión al publicar el evento.");

    public Task<IReadOnlyList<EventoSistema>> ObtenerPendientesAsync(
        int maximo, IReadOnlyCollection<string> tipos, CancellationToken ct = default) =>
        interno.ObtenerPendientesAsync(maximo, tipos, ct);

    public Task MarcarProcesadoAsync(long eventoId, CancellationToken ct = default) =>
        interno.MarcarProcesadoAsync(eventoId, ct);

    public Task MarcarFallidoAsync(long eventoId, string error, int reintentosMaximos, CancellationToken ct = default) =>
        interno.MarcarFallidoAsync(eventoId, error, reintentosMaximos, ct);

    public Task<int> PurgarProcesadosAsync(int dias, int tamanoLote, CancellationToken ct = default) =>
        interno.PurgarProcesadosAsync(dias, tamanoLote, ct);

    public Task<int> AnonimizarPorConversacionAsync(
        IReadOnlyCollection<int> conversacionIds, CancellationToken ct = default) =>
        interno.AnonimizarPorConversacionAsync(conversacionIds, ct);
}
