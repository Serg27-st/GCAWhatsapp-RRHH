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
        // general, con la fecha desde la que espera, para que cualquier analista la tome (FUN-06).
        if (ctx.AnalistaRespaldo is null)
        {
            return Task.FromResult(ResultadoRegla.Con(
                new DerivarAPendientes(
                    $"La cuenta {cuenta.Nombre} no tiene respaldo configurado y su titular esta ausente.")));
        }

        var acciones = new List<AccionRegla>();

        if (conversacion.CuentaContextoId != cuenta.CuentaId)
            acciones.Add(new EstablecerCuentaContexto(cuenta.CuentaId));

        // COR-08 (AL4): la ausencia enruta lo que nadie atiende, o lo que atendia el titular que se
        // fue. Si el hilo lo tomo un tercero —por transferencia (Regla 8) o por escalamiento (Regla
        // 2)—, reasignarlo al respaldo en cada mensaje deshacia esa decision sola.
        var atiendeOtro = conversacion.AnalistaAtendiendoId is { } atendiendo
            && atendiendo != ctx.AnalistaTitular?.AnalistaId;

        if (!atiendeOtro)
        {
            acciones.Add(new AsignarAnalista(
                ctx.AnalistaRespaldo.AnalistaId,
                $"Titular de la cuenta {cuenta.Nombre} ausente: se asigna al respaldo."));

            acciones.Add(new RegistrarAuditoria(
                "AsignacionPorAusencia",
                $"Conversacion {conversacion.ConversacionId} asignada al respaldo {ctx.AnalistaRespaldo.Nombre}."));

            // Regla 6: quien recibe el hilo tiene que saber que el postulante esta en otras cuentas.
            acciones.AddRange(AvisoMultiCuenta.Acciones(ctx, ctx.AnalistaRespaldo.AnalistaId));
        }

        // V30: con cuenta identificada y analista, el hilo sale del menu del bot o de «Sin clasificar».
        if (conversacion.Estado is EstadoConversacion.EnMenuBot or EstadoConversacion.PendienteClasificar)
            acciones.Add(new CambiarEstadoConversacion(EstadoConversacion.Activa));

        // COR-06: el hilo pasa a una persona, asi que los intentos del menu dejan de correr.
        if (conversacion.IntentosMenuFallidos > 0 || conversacion.FechaTextoNoReconocido is not null)
            acciones.Add(new ReiniciarIntentosMenu());

        return Task.FromResult(acciones.Count == 0
            ? ResultadoRegla.SinAccion
            : ResultadoRegla.Con([.. acciones]));
    }
}
