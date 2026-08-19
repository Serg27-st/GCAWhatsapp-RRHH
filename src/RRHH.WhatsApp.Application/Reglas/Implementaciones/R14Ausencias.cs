using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas.Implementaciones;

/// <summary>
/// Regla 14 — Ausencias planificadas.
/// <para>
/// Mientras dure una ausencia registrada (vacaciones o descanso medico), las conversaciones
/// nuevas de las cuentas del analista van directo al respaldo, sin esperar las 2 horas de la
/// Regla 2. Es el complemento excluyente de <see cref="R01Asignacion"/>: una aplica cuando el
/// titular esta disponible y la otra cuando no, de modo que nunca compiten por asignar.
/// </para>
/// </summary>
public sealed class R14Ausencias : IReglaNegocio
{
    public string Codigo => "R14";

    public string Descripcion => "Enruta al respaldo directamente cuando el analista titular esta ausente.";

    /// <summary>Antes que la Regla 1, para resolver el destino antes de cualquier asignacion.</summary>
    public int Prioridad => 20;

    public bool Aplica(ContextoRegla ctx) =>
        ctx.Disparador is TipoDisparador.MensajeEntrante or TipoDisparador.JobFormsCompletado
        && ctx.Conversacion is not null
        && ctx.Cuenta is not null
        && ctx.TitularAusente;

    public Task<ResultadoRegla> EvaluarAsync(ContextoRegla ctx, CancellationToken ct = default)
    {
        var conversacion = ctx.Conversacion!;
        var cuenta = ctx.Cuenta!;

        // Sin respaldo configurado la conversacion no puede quedar en el aire: pasa a la bandeja
        // general para que cualquier analista la tome (mismo destino que la Regla 19).
        if (ctx.AnalistaRespaldo is null)
        {
            return Task.FromResult(ResultadoRegla.Con(
                new CambiarEstadoConversacion(EstadoConversacion.PendienteClasificar),
                new RegistrarAuditoria(
                    "AusenciaSinRespaldo",
                    $"La cuenta {cuenta.Nombre} no tiene respaldo configurado y su titular esta ausente.")));
        }

        var acciones = new List<AccionRegla>
        {
            new EstablecerCuentaContexto(cuenta.CuentaId),
            new AsignarAnalista(
                ctx.AnalistaRespaldo.AnalistaId,
                $"Titular de la cuenta {cuenta.Nombre} ausente: se asigna al respaldo."),
            new RegistrarAuditoria(
                "AsignacionPorAusencia",
                $"Conversacion {conversacion.ConversacionId} asignada al respaldo {ctx.AnalistaRespaldo.Nombre}.")
        };

        if (conversacion.Estado == EstadoConversacion.PendienteClasificar)
            acciones.Add(new CambiarEstadoConversacion(EstadoConversacion.Activa));

        return Task.FromResult(ResultadoRegla.Con([.. acciones]));
    }
}
