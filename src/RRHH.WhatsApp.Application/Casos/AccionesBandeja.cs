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
        bool enviarCierre = true,
        CancellationToken ct = default)
    {
        var descartadas = await postulaciones.MarcarEstadoAsync(
            postulanteId, cuentaId, tipo, motivo, analistaId, ct);

        foreach (var postulacionId in descartadas)
            await PublicarDescarteAsync(postulacionId, analistaId, enviarCierre, ct);

        log.LogInformation(
            "Postulante {PostulanteId} marcado {Tipo} en la cuenta {CuentaId} por el analista {AnalistaId}.",
            postulanteId, tipo, cuentaId, analistaId);
    }

    public async Task MoverEtapaAsync(
        int postulacionId, int etapaId, int analistaId, bool enviarCierre = true, CancellationToken ct = default)
    {
        // COR-11 (P4): el descarte se publica solo cuando la tarjeta **pasa** a descartada. Publicarlo
        // por el estado final repetia el cierre de cortesia cada vez que alguien tocaba una tarjeta
        // que ya estaba en esa columna.
        var movimiento = await postulaciones.MoverEtapaKanbanAsync(postulacionId, etapaId, analistaId, ct);

        if (movimiento is { Aplicado: true, Nuevo: EstadoPostulacion.Descartado }
            && movimiento.Anterior != EstadoPostulacion.Descartado)
        {
            await PublicarDescarteAsync(postulacionId, analistaId, enviarCierre, ct);
        }
    }

    /// <param name="enviarCierre">
    /// A11: lo que el analista dejo marcado en el dialogo de descarte. Viaja en el evento porque es la
    /// decision de ese momento, y quien la ejecuta es el motor de reglas (FUN-10).
    /// </param>
    private Task PublicarDescarteAsync(int postulacionId, int analistaId, bool enviarCierre, CancellationToken ct) =>
        eventos.PublicarAsync(
            TiposEvento.PostulacionDescartada,
            new { PostulacionId = postulacionId, AnalistaId = analistaId, EnviarCierre = enviarCierre },
            Guid.NewGuid(),
            ct);
}
