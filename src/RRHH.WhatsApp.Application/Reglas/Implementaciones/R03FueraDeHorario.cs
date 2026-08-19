using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas.Implementaciones;

/// <summary>
/// Regla 3 — Fuera de horario.
/// <para>
/// Se avisa el horario de atencion, pero el flujo sigue activo: la conversacion se asigna igual y
/// cualquier analista puede responder si lo desea. Por eso esta regla solo agrega un envio y nunca
/// detiene la evaluacion ni cambia el estado de la conversacion.
/// </para>
/// <para>
/// El aviso se manda una sola vez por hilo mientras dure el periodo fuera de horario: repetirlo en
/// cada mensaje es justo el tipo de ruido que eleva los reportes de spam descritos en la Seccion 2.4.
/// </para>
/// </summary>
public sealed class R03FueraDeHorario : IReglaNegocio
{
    public string Codigo => "R03";

    public string Descripcion => "Informa el horario de atencion cuando el mensaje llega fuera de jornada.";

    /// <summary>Despues de asignar, para que el aviso salga junto con el enrutamiento ya resuelto.</summary>
    public int Prioridad => 50;

    public bool Aplica(ContextoRegla ctx) =>
        ctx.Disparador == TipoDisparador.MensajeEntrante
        && ctx.Conversacion is not null
        && !ctx.DentroDeHorario;

    public Task<ResultadoRegla> EvaluarAsync(ContextoRegla ctx, CancellationToken ct = default)
    {
        var conversacion = ctx.Conversacion!;

        // Si ya hubo actividad reciente en el hilo, el postulante probablemente ya recibio el
        // aviso en este mismo periodo fuera de horario.
        var yaAvisadoEnEstaTanda =
            conversacion.FechaUltimoMensajeEntrante is { } ultimoEntrante
            && (ctx.AhoraUtc - ultimoEntrante) < TimeSpan.FromHours(8);

        if (yaAvisadoEnEstaTanda)
            return Task.FromResult(ResultadoRegla.SinAccion);

        var descripcionHorario = ctx.Configuracion.TryGetValue("horario.descripcion", out var d)
            ? d
            : "de lunes a viernes de 9:00 a 18:00";

        return Task.FromResult(ResultadoRegla.Con(
            new EnviarPlantilla(ClavesPlantilla.FueraDeHorario, [descripcionHorario])));
    }
}
