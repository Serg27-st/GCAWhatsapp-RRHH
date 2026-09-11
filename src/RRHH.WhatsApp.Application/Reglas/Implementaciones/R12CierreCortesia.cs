using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas.Implementaciones;

/// <summary>
/// Regla 12 — Mensaje de cierre automatizado.
/// <para>
/// Cuando el postulante queda descartado, el sistema manda solo el mensaje de cortesia, sin que el
/// analista tenga que redactarlo cada vez. Va como plantilla y no como texto libre a proposito: el
/// descarte suele decidirse dias despues del ultimo mensaje del postulante, con la ventana de 24h
/// ya cerrada.
/// </para>
/// </summary>
public sealed class R12CierreCortesia : IReglaNegocio
{
    public string Codigo => "R12";

    public string Descripcion => "Manda el mensaje de cierre de cortesia cuando la postulacion queda descartada.";

    /// <summary>Unica regla de su disparador; el numero solo la ordena frente a futuras.</summary>
    public int Prioridad => 60;

    public bool Aplica(ContextoRegla ctx) =>
        ctx.Disparador == TipoDisparador.CambioEstadoPostulacion
        && ctx.Conversacion is not null
        && ctx.Postulacion is { Estado: EstadoPostulacion.Descartado };

    public Task<ResultadoRegla> EvaluarAsync(ContextoRegla ctx, CancellationToken ct = default)
    {
        var vacante = ctx.Hc?.Titulo ?? ctx.Postulacion?.Hc?.Titulo ?? "la vacante";

        return Task.FromResult(ResultadoRegla.Con(
            new EnviarPlantilla(
                ClavesPlantilla.CierreCortesia,
                [ctx.Postulante?.NombreCompleto ?? "hola", vacante]),
            new RegistrarAuditoria(
                "CierreCortesia",
                $"Postulacion {ctx.Postulacion!.PostulacionId} descartada: se envio el cierre de cortesia.")));
    }
}
