using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas.Implementaciones;

/// <summary>
/// Regla 20 — Vacante cerrada.
/// <para>
/// Si el HC elegido ya fue cubierto, el bot lo dice y vuelve a ofrecer opciones: las otras
/// vacantes de la misma cuenta si quedan, y el menu de empresas si la cuenta se quedo sin
/// ninguna. Lo que no puede pasar es que el postulante reciba un enlace muerto y llene un
/// formulario que no lleva a ninguna parte.
/// </para>
/// </summary>
public sealed class R20VacanteCerrada : IReglaNegocio
{
    public string Codigo => "R20";

    public string Descripcion => "Avisa que la vacante ya no esta disponible y vuelve a ofrecer opciones.";

    /// <summary>
    /// Antes de que la Regla 9 mande el enlace: el punto de la regla es que ese envio no ocurra.
    /// </summary>
    public int Prioridad => 42;

    public bool Aplica(ContextoRegla ctx) =>
        ctx.Disparador == TipoDisparador.MensajeEntrante
        && ctx.Conversacion is not null
        && ctx.Cuenta is not null
        && (ctx.Hc is { Estado: EstadoHc.Cerrada } || ctx.VacantesAbiertas.Count == 0);

    public Task<ResultadoRegla> EvaluarAsync(ContextoRegla ctx, CancellationToken ct = default)
    {
        var cuenta = ctx.Cuenta!;
        var vacante = ctx.Hc?.Titulo ?? $"que buscabas en {cuenta.Nombre}";

        var acciones = new List<AccionRegla>
        {
            // COR-03 (P1): el aviso llega en texto mientras la ventana esta abierta, que es lo normal
            // aca —el postulante acaba de escribir— y no depende de que Meta aprobara la plantilla.
            new EnviarMensajeBot(
                TextosBot.VacanteCerrada(vacante),
                ClavesPlantilla.VacanteCerrada,
                [vacante])
        };

        if (ctx.VacantesAbiertas.Count > 0)
        {
            // La cuenta sigue teniendo que ofrecer: se queda donde esta y elige otra vacante.
            acciones.Add(new MostrarMenuVacantes(cuenta.CuentaId));
        }
        else
        {
            // Sin vacantes abiertas la cuenta ya no sirve de contexto; se suelta para que el menu
            // de empresas pueda volver a empezar.
            acciones.Add(new LimpiarCuentaContexto($"La cuenta {cuenta.Nombre} no tiene vacantes abiertas."));
            acciones.Add(new MostrarMenuEmpresas(EsReintento: false));
        }

        acciones.Add(new RegistrarAuditoria(
            "VacanteCerrada",
            $"Se informo al postulante que no hay vacante disponible en {cuenta.Nombre}."));

        // Detiene: lo que sigue es el envio del enlace, que es justo lo que esta regla evita.
        return Task.FromResult(ResultadoRegla.Detener([.. acciones]));
    }
}
