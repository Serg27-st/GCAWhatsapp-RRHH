using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas.Implementaciones;

/// <summary>
/// Regla 2 — Escalamiento por inactividad.
/// <para>
/// Si el analista dueno de la cuenta no responde dentro del plazo configurado, la conversacion
/// pasa al analista de respaldo fijo de esa cuenta (no a uno aleatorio).
/// </para>
/// <para>
/// El plazo puede contarse a reloj corrido o solo dentro del horario de atencion, segun
/// <see cref="ClavesConfiguracion.EscalamientoSoloHorarioLaboral"/>. Con el valor por defecto
/// (true) un mensaje recibido al cierre de la jornada no escala de madrugada hacia un respaldo
/// que tampoco esta disponible.
/// </para>
/// </summary>
public sealed class R02Escalamiento : IReglaNegocio
{
    public string Codigo => "R02";

    public string Descripcion => "Escala al respaldo fijo de la cuenta si el titular no responde en el plazo configurado.";

    public int Prioridad => 25;

    public bool Aplica(ContextoRegla ctx) =>
        ctx.Disparador == TipoDisparador.TiempoTranscurrido
        && ctx.Conversacion is { Estado: EstadoConversacion.Activa, AnalistaAtendiendoId: not null }
        && ctx.AnalistaRespaldo is not null
        // Una conversacion ya escalada no vuelve a escalar: el respaldo es el ultimo eslabon.
        && ctx.Conversacion.AnalistaAtendiendoId != ctx.AnalistaRespaldo.AnalistaId;

    public Task<ResultadoRegla> EvaluarAsync(ContextoRegla ctx, CancellationToken ct = default)
    {
        var soloHorarioLaboral = ctx.ConfigBool(ClavesConfiguracion.EscalamientoSoloHorarioLaboral, true);
        var horasLimite = ctx.ConfigInt(ClavesConfiguracion.EscalamientoHoras, 2);

        var minutosTranscurridos = soloHorarioLaboral
            ? ctx.MinutosSinRespuestaHabiles
            : ctx.MinutosSinRespuestaReloj;

        // Nulo significa que no hay nada pendiente de responder: el analista ya contesto el
        // ultimo mensaje del postulante.
        if (minutosTranscurridos is not { } minutos)
            return Task.FromResult(ResultadoRegla.SinAccion);

        if (minutos < horasLimite * 60)
            return Task.FromResult(ResultadoRegla.SinAccion);

        var respaldo = ctx.AnalistaRespaldo!;
        var unidad = soloHorarioLaboral ? "horas habiles" : "horas";

        return Task.FromResult(ResultadoRegla.Con(
            new EscalarARespaldo(
                respaldo.AnalistaId,
                $"El titular no respondio en {horasLimite} {unidad}."),
            new CambiarEstadoConversacion(EstadoConversacion.Escalada),
            new NotificarAnalista(
                respaldo.AnalistaId,
                "Recibiste una conversacion escalada por falta de respuesta del analista titular."),
            new RegistrarAuditoria(
                "EscalamientoPorInactividad",
                $"Conversacion {ctx.Conversacion!.ConversacionId} escalada tras {minutos:F0} minutos ({unidad}).")));
    }
}
