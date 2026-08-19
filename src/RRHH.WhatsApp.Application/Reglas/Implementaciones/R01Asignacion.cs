using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas.Implementaciones;

/// <summary>
/// Regla 1 — Asignacion.
/// <para>
/// Identificada la cuenta a la que pertenece el mensaje, la conversacion se enruta al analista
/// responsable de esa cuenta. Los 2 analistas que hoy reparten manualmente dejan de ser el filtro
/// (Regla 5): esta regla asume el 100% de esa funcion.
/// </para>
/// <para>
/// No aplica cuando el titular esta ausente: ese caso lo atiende <see cref="R14Ausencias"/>,
/// que enruta directo al respaldo sin esperar las 2 horas de la Regla 2.
/// </para>
/// </summary>
public sealed class R01Asignacion : IReglaNegocio
{
    public string Codigo => "R01";

    public string Descripcion => "Enruta la conversacion al analista responsable de la cuenta identificada.";

    public int Prioridad => 30;

    public bool Aplica(ContextoRegla ctx) =>
        ctx.Disparador is TipoDisparador.MensajeEntrante or TipoDisparador.JobFormsCompletado
        && ctx.Conversacion is not null
        && ctx.Cuenta is not null
        && ctx.AnalistaTitular is not null
        && !ctx.TitularAusente;

    public Task<ResultadoRegla> EvaluarAsync(ContextoRegla ctx, CancellationToken ct = default)
    {
        var conversacion = ctx.Conversacion!;
        var titular = ctx.AnalistaTitular!;
        var cuenta = ctx.Cuenta!;

        var acciones = new List<AccionRegla>();

        if (conversacion.CuentaContextoId != cuenta.CuentaId)
            acciones.Add(new EstablecerCuentaContexto(cuenta.CuentaId));

        // Reasignar en cada mensaje devolveria al titular una conversacion que otro analista ya
        // tomo por transferencia (Regla 8) o por escalamiento (Regla 2).
        if (conversacion.AnalistaAtendiendoId is null)
        {
            acciones.Add(new AsignarAnalista(
                titular.AnalistaId,
                $"Analista responsable de la cuenta {cuenta.Nombre}."));
        }

        if (conversacion.Estado == EstadoConversacion.PendienteClasificar)
            acciones.Add(new CambiarEstadoConversacion(EstadoConversacion.Activa));

        // Regla 6: el analista debe saber que el mismo DNI esta en proceso con otras cuentas,
        // pero sin ver el detalle de esas conversaciones.
        if (ctx.OtrasCuentasEnProceso.Count > 0)
        {
            var nombres = string.Join(", ", ctx.OtrasCuentasEnProceso.Select(c => c.Nombre));
            acciones.Add(new NotificarAnalista(
                titular.AnalistaId,
                $"Este postulante tambien esta en proceso con: {nombres}."));
        }

        return Task.FromResult(acciones.Count == 0
            ? ResultadoRegla.SinAccion
            : ResultadoRegla.Con([.. acciones]));
    }
}
