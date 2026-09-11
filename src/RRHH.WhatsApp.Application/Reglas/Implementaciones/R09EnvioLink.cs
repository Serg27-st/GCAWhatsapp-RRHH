using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas.Implementaciones;

/// <summary>
/// Regla 9 — envio del enlace del JobForms.
/// <para>
/// Una vez identificada la empresa, el paso siguiente del flujo es mandarle al postulante el
/// formulario de esa vacante. Si la cuenta tiene una sola vacante abierta se manda directo; si
/// tiene varias, primero hay que preguntar a cual, porque el formulario y sus preguntas son por
/// HC y no por cliente.
/// </para>
/// </summary>
public sealed class R09EnvioLink : IReglaNegocio
{
    public string Codigo => "R09";

    public string Descripcion => "Manda el enlace del JobForms de la vacante elegida.";

    /// <summary>Despues de asignar y de descartar vacantes cerradas, antes del aviso de horario.</summary>
    public int Prioridad => 45;

    public bool Aplica(ContextoRegla ctx) =>
        ctx.Disparador == TipoDisparador.MensajeEntrante
        && ctx.Conversacion is not null
        && ctx.Cuenta is not null
        && ctx.VacantesAbiertas.Count > 0;

    public Task<ResultadoRegla> EvaluarAsync(ContextoRegla ctx, CancellationToken ct = default)
    {
        var cuenta = ctx.Cuenta!;

        // La vacante elegida por boton manda. Si no eligio y hay una sola abierta, no tiene sentido
        // preguntarle cual.
        var vacante = ctx.Hc is { Estado: EstadoHc.Abierta } elegida
            ? elegida
            : ctx.VacantesAbiertas.Count == 1 ? ctx.VacantesAbiertas[0] : null;

        if (vacante is null)
            return Task.FromResult(ResultadoRegla.Con(new MostrarMenuVacantes(cuenta.CuentaId)));

        // Ya se le mando el enlace de esta vacante en este hilo. Repetirlo en cada mensaje es
        // ruido, y despues de que completo el formulario ademas seria confuso.
        if (ctx.HcsConInvitacion.Contains(vacante.HcId))
            return Task.FromResult(ResultadoRegla.SinAccion);

        return Task.FromResult(ResultadoRegla.Con(
            new EnviarLinkJobForms(vacante.HcId),
            new RegistrarAuditoria(
                "EnlaceJobFormsEnviado",
                $"Vacante {vacante.HcId} ({vacante.Titulo}) de la cuenta {cuenta.Nombre}.")));
    }
}
