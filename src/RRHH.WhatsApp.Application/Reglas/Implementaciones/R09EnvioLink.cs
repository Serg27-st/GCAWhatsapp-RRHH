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
/// <para>
/// El menu de vacantes se ofrece una sola vez (COR-02, C2): quien ya eligio —o ya completo el
/// formulario, o ya tiene un proceso vivo en la cuenta— esta conversando, y repetirle el menu en
/// cada mensaje es el ruido que termina en reportes de spam (P4).
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
        && ctx.VacantesAbiertas.Count > 0
        // Un hilo que ya atiende una persona sobre un proceso vivo de esta cuenta no es trabajo del
        // bot: lo que el postulante escriba ahi va para el analista, no para el menu.
        && !(ctx.Conversacion is { Estado: EstadoConversacion.Activa, AnalistaAtendiendoId: not null }
             && ctx.CuentasVivas.Contains(ctx.Cuenta.CuentaId));

    public Task<ResultadoRegla> EvaluarAsync(ContextoRegla ctx, CancellationToken ct = default)
    {
        var cuenta = ctx.Cuenta!;

        // Solo cuenta como eleccion la que hizo el postulante en este mensaje: un boton o el codigo
        // de aviso. La vacante que quedo en el contexto de un mensaje anterior no autoriza a
        // mandarle otra vez nada (AL2).
        var eligioAhora = ctx.OrigenEleccion is OrigenEleccion.Boton or OrigenEleccion.CodigoAviso;

        var vacante = eligioAhora && ctx.Hc is { Estado: EstadoHc.Abierta } elegida ? elegida : null;

        if (vacante is null)
        {
            // Ya esta conversando sobre alguna vacante de la cuenta: con invitacion enviada o con un
            // proceso vivo. No hay nada que ofrecerle.
            var yaEmpezo =
                ctx.HcsConInvitacion.Any(hcId => ctx.VacantesAbiertas.Any(v => v.HcId == hcId))
                || ctx.CuentasVivas.Contains(cuenta.CuentaId);

            if (yaEmpezo)
                return Task.FromResult(ResultadoRegla.SinAccion);

            // Con varias vacantes hay que preguntar cual; con una sola, preguntar seria un menu de
            // una opcion.
            if (ctx.VacantesAbiertas.Count > 1)
                return Task.FromResult(ResultadoRegla.Con(new MostrarMenuVacantes(cuenta.CuentaId)));

            vacante = ctx.VacantesAbiertas[0];
        }

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
