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
        && (
            (ctx.Cuenta is not null && ctx.AnalistaTitular is not null && !ctx.TitularAusente)
            // FUN-09: el postulante eligio uno de sus procesos en el menu de desambiguacion.
            || ctx.PostulacionElegidaId is not null
            // FUN-08 (A2): sin cuenta identificada pero con un unico proceso vivo, no hay nada que
            // preguntar: el hilo es de ese proceso y de su analista.
            || (ctx.Cuenta is null && ctx.OrigenEleccion == OrigenEleccion.Ninguna && UnicoProcesoVivo(ctx) is not null));

    /// <summary>La unica postulacion viva del postulante, o nula si no hay ninguna o hay varias (esas las resuelve la R06).</summary>
    private static PostulacionVigente? UnicoProcesoVivo(ContextoRegla ctx)
    {
        var vivos = ctx.PostulacionesDelPostulante
            .Where(p => p.Estado is EstadoPostulacion.EnProceso or EstadoPostulacion.Reingreso)
            .ToList();

        return vivos.Count == 1 ? vivos[0] : null;
    }

    public Task<ResultadoRegla> EvaluarAsync(ContextoRegla ctx, CancellationToken ct = default)
    {
        var conversacion = ctx.Conversacion!;

        // FUN-09: el postulante eligio uno de sus procesos en el menu de desambiguacion. Manda sobre
        // cualquier contexto anterior: es exactamente lo que se le pregunto.
        if (ctx.PostulacionElegidaId is { } elegida)
        {
            return Task.FromResult(ResultadoRegla.Con(
                new TomarContextoDePostulacion(elegida),
                new ReiniciarIntentosMenu(),
                new RegistrarAuditoria(
                    "ProcesoElegido",
                    $"El postulante eligio continuar con la postulacion {elegida}.")));
        }

        // FUN-08: el hilo perdio el contexto —la Regla 9 lo limpio, o es un reingreso que volvio a
        // escribir— pero la persona tiene un solo proceso vivo. Se retoma ese, con su analista.
        if (ctx.Cuenta is null && UnicoProcesoVivo(ctx) is { } proceso)
        {
            return Task.FromResult(ResultadoRegla.Con(
                new TomarContextoDePostulacion(proceso.PostulacionId),
                new RegistrarAuditoria(
                    "ContinuidadDeProceso",
                    $"El postulante tiene un unico proceso vivo ({proceso.Cuenta}): se retoma sin menu.")));
        }
        var titular = ctx.AnalistaTitular!;
        var cuenta = ctx.Cuenta!;

        var acciones = new List<AccionRegla>();

        if (conversacion.CuentaContextoId != cuenta.CuentaId)
            acciones.Add(new EstablecerCuentaContexto(cuenta.CuentaId));

        // Reasignar en cada mensaje devolveria al titular una conversacion que otro analista ya
        // tomo por transferencia (Regla 8) o por escalamiento (Regla 2).
        var asigna = conversacion.AnalistaAtendiendoId is null;

        if (asigna)
        {
            acciones.Add(new AsignarAnalista(
                titular.AnalistaId,
                $"Analista responsable de la cuenta {cuenta.Nombre}."));
        }

        // V30: con cuenta identificada y analista, el hilo sale del menu del bot o de «Sin clasificar».
        if (conversacion.Estado is EstadoConversacion.EnMenuBot or EstadoConversacion.PendienteClasificar)
            acciones.Add(new CambiarEstadoConversacion(EstadoConversacion.Activa));

        // COR-06: el postulante eligio una cuenta, asi que los intentos del menu dejan de correr. Si
        // vuelve a quedarse sin contexto mas adelante, empieza de cero y ve el menu otra vez (AL2).
        if (conversacion.IntentosMenuFallidos > 0 || conversacion.FechaTextoNoReconocido is not null)
            acciones.Add(new ReiniciarIntentosMenu());

        // Regla 6, COR-10 (AL9, P4): el aviso sale cuando hay algo nuevo que contar —recien se asigno
        // el hilo, o acaba de nacer una postulacion—, no en cada mensaje. Tres entrantes seguidos del
        // mismo postulante multi-cuenta producian tres avisos identicos.
        var naceUnaPostulacion = ctx.Disparador == TipoDisparador.JobFormsCompletado;

        if (asigna || naceUnaPostulacion)
        {
            acciones.AddRange(AvisoMultiCuenta.Acciones(
                ctx, titular.AnalistaId, notificarOtrasCuentas: naceUnaPostulacion));
        }

        return Task.FromResult(acciones.Count == 0
            ? ResultadoRegla.SinAccion
            : ResultadoRegla.Con([.. acciones]));
    }
}
