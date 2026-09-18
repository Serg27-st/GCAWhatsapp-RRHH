using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas.Implementaciones;

/// <summary>
/// Regla 6 — desambiguacion multi-cuenta (FUN-09, A3).
/// <para>
/// WhatsApp entrega un unico hilo por telefono (V1), pero la misma persona puede estar en proceso con
/// dos clientes a la vez. Cuando escribe sin decir por cual, el bot no puede adivinar: le ofrece sus
/// procesos por nombre y la salida «Otra empresa».
/// </para>
/// <para>
/// Antes se asumia la cuenta que hubiera quedado en el contexto, asi que un mensaje sobre Intradevco
/// terminaba en la bandeja del analista de Alicorp, que no tenia como saberlo (R11).
/// </para>
/// </summary>
public sealed class R06Desambiguacion : IReglaNegocio
{
    public string Codigo => "R06";

    public string Descripcion => "Pregunta por cual de sus procesos escribe cuando el postulante esta en varias cuentas.";

    /// <summary>Antes del menu de empresas y de la repregunta: preguntar por sus procesos es mas preciso.</summary>
    public int Prioridad => 17;

    public bool Aplica(ContextoRegla ctx)
    {
        if (ctx.Disparador != TipoDisparador.MensajeEntrante || ctx.Conversacion is null)
            return false;

        // Si eligio algo en este mensaje —un boton de cuenta, de vacante o de proceso, o el codigo
        // del aviso—, ya dijo por cual escribe.
        if (ctx.OrigenEleccion is not (OrigenEleccion.Ninguna or OrigenEleccion.Contexto)
            || ctx.PostulacionElegidaId is not null
            || ctx.PidioOtraEmpresa)
        {
            return false;
        }

        if (ctx.CuentasVivas.Count < 2)
            return false;

        // Con el contexto fresco se le sigue respondiendo por donde venia: preguntar en cada mensaje
        // seria el ruido que la Regla 6 justamente evita (P4). Pasados los dias configurados, ya no
        // se puede suponer que sigue hablando de lo mismo (A13).
        var dias = ctx.ConfigInt(ClavesConfiguracion.RepreguntaEmpresaDias, 3);

        return ctx.Conversacion.CuentaContextoId is null
            || ctx.DiasDesdeActividadAnterior >= dias;
    }

    public Task<ResultadoRegla> EvaluarAsync(ContextoRegla ctx, CancellationToken ct = default) =>
        // Detiene: lo que sigue enrutaria el hilo a una cuenta que todavia no se sabe cual es.
        Task.FromResult(ResultadoRegla.Detener(
            new MostrarMenuProcesos(),
            new ReiniciarIntentosMenu(),
            new RegistrarAuditoria(
                "DesambiguacionMultiCuenta",
                $"El postulante tiene procesos vivos en {ctx.CuentasVivas.Count} cuentas: se le pregunta por cual escribe.")));
}
