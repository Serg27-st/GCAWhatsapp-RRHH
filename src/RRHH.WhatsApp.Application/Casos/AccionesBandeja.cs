using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Application.Casos;

/// <summary>
/// Las acciones que el analista dispara desde la bandeja y que ademas pueden desencadenar reglas:
/// marcar whitelist o blacklist (Regla 7) y mover la tarjeta del kanban (Regla 13).
/// <para>
/// Ninguna manda mensajes por su cuenta. Cuando la accion descarta una postulacion, publican el
/// evento y el motor decide si corresponde el cierre de cortesia de la Regla 12: mandar el mensaje
/// desde aca convertiria una decision de negocio en un efecto colateral del endpoint.
/// </para>
/// </summary>
public sealed class AccionesBandeja(
    IPostulacionService postulaciones,
    IEventoSistemaService eventos,
    ILogger<AccionesBandeja> log)
{
    public async Task MarcarAsync(
        int postulanteId,
        int cuentaId,
        TipoEstadoPostulante tipo,
        string? motivo,
        int analistaId,
        CancellationToken ct = default)
    {
        var descartadas = await postulaciones.MarcarEstadoAsync(
            postulanteId, cuentaId, tipo, motivo, analistaId, ct);

        foreach (var postulacionId in descartadas)
            await PublicarDescarteAsync(postulacionId, analistaId, ct);

        log.LogInformation(
            "Postulante {PostulanteId} marcado {Tipo} en la cuenta {CuentaId} por el analista {AnalistaId}.",
            postulanteId, tipo, cuentaId, analistaId);
    }

    public async Task MoverEtapaAsync(
        int postulacionId, int etapaId, int analistaId, CancellationToken ct = default)
    {
        await postulaciones.MoverEtapaKanbanAsync(postulacionId, etapaId, analistaId, ct);

        // Solo interesa el desenlace: arrastrar la tarjeta a Descartado es la otra via por la que
        // el postulante queda fuera, y merece el mismo cierre de cortesia que la blacklist.
        var estado = await postulaciones.ObtenerEstadoAsync(postulacionId, ct);

        if (estado == EstadoPostulacion.Descartado)
            await PublicarDescarteAsync(postulacionId, analistaId, ct);
    }

    private Task PublicarDescarteAsync(int postulacionId, int analistaId, CancellationToken ct) =>
        eventos.PublicarAsync(
            TiposEvento.PostulacionDescartada,
            new { PostulacionId = postulacionId, AnalistaId = analistaId },
            Guid.NewGuid(),
            ct);
}
