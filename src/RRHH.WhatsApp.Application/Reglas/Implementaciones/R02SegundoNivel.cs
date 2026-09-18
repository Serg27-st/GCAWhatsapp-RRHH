using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas.Implementaciones;

/// <summary>
/// Regla 2 — segundo nivel de escalamiento (FUN-05, A9).
/// <para>
/// Escalar al respaldo no puede ser el final del camino: si el respaldo tampoco responde, el hilo se
/// queda esperando sin que nadie lo sepa. Pasado el plazo, se avisa a Jefatura y a los dos analistas
/// involucrados, para que alguien decida (P3: ningun hilo sin dueño ni plazo).
/// </para>
/// <para>
/// El plazo corre en horas habiles: un escalamiento del viernes a la tarde no avisa el sabado, cuando
/// nadie de Jefatura va a poder hacer nada con el aviso.
/// </para>
/// </summary>
public sealed class R02SegundoNivel : IReglaNegocio
{
    public string Codigo => "R02";

    public string Descripcion => "Avisa a Jefatura si el respaldo tampoco responde tras el escalamiento.";

    /// <summary>Despues de la R02 que escala: primero se escala, y recien el barrido siguiente mide este plazo.</summary>
    public int Prioridad => 26;

    public bool Aplica(ContextoRegla ctx) =>
        ctx.Disparador == TipoDisparador.TiempoTranscurrido
        && ctx.Conversacion is
        {
            Estado: EstadoConversacion.Escalada,
            FechaAvisoSegundoNivel: null,
            FechaEscalamiento: not null
        }
        // Lo que se mide es la espera del postulante: si el respaldo ya contesto, no hay nada que avisar.
        && ctx.Conversacion.FechaUltimoMensajeEntrante is { } entrante
        && (ctx.Conversacion.FechaUltimaRespuestaAnalista is not { } respuesta || respuesta < entrante);

    public Task<ResultadoRegla> EvaluarAsync(ContextoRegla ctx, CancellationToken ct = default)
    {
        var horas = ctx.ConfigInt(ClavesConfiguracion.EscalamientoHorasSegundoNivel, 2);

        // Nulo significa que la fabrica no pudo medir el tramo habil: sin numero no se avisa, igual
        // que la Regla 2 con su propio plazo.
        if (ctx.MinutosHabilesDesdeEscalamiento is not { } minutos || minutos < horas * 60)
            return Task.FromResult(ResultadoRegla.SinAccion);

        var cuenta = ctx.Cuenta?.Nombre ?? "una cuenta sin identificar";
        var acciones = new List<AccionRegla>
        {
            // A Jefatura por rol y no por persona: quien la ocupa cambia sin que cambie la regla.
            new NotificarRol(
                RolAnalista.Jefatura,
                $"La conversacion de {cuenta} sigue sin respuesta tras escalar al respaldo.")
        };

        // Los dos analistas involucrados: el respaldo, que la tiene ahora, y el titular, que es el
        // dueño de la cuenta y tiene que saber que su hilo llego a Jefatura.
        foreach (var analistaId in new[] { ctx.Conversacion!.AnalistaAtendiendoId, ctx.AnalistaTitular?.AnalistaId }
                     .Where(id => id is not null)
                     .Select(id => id!.Value)
                     .Distinct())
        {
            acciones.Add(new NotificarAnalista(
                analistaId,
                $"El postulante de {cuenta} sigue esperando respuesta: el caso paso a Jefatura."));
        }

        acciones.Add(new SellarConversacion(MarcaConversacion.AvisoSegundoNivel));

        acciones.Add(new RegistrarAuditoria(
            "EscalamientoSegundoNivel",
            $"Sin respuesta del respaldo tras {minutos:F0} minutos habiles desde el escalamiento."));

        return Task.FromResult(ResultadoRegla.Con([.. acciones]));
    }
}
