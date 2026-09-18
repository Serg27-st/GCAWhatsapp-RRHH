using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas.Implementaciones;

/// <summary>
/// Regla 9 — repregunta de empresa tras inactividad.
/// <para>
/// Si el postulante vuelve a escribir despues de los dias configurados, el contexto de cuenta
/// anterior deja de ser confiable: pudo haber quedado de otra postulacion. El bot vuelve a
/// preguntar la empresa, salvo que siga teniendo un proceso vivo ahi.
/// </para>
/// <para>
/// Lo que manda es el proceso, no el estado suelto (COR-07, AL3). Antes se repreguntaba a quien
/// estaba <c>EnProceso</c> —y limpiarle el contexto le quitaba su analista— mientras que un
/// <c>Descartado</c> en cualquier otra cuenta le bloqueaba el menu para siempre.
/// </para>
/// <para>
/// Detiene la evaluacion a proposito: dejar correr a la Regla 1 volveria a enrutar la conversacion
/// hacia la cuenta que se acaba de descartar por vieja.
/// </para>
/// </summary>
public sealed class R09RepreguntaEmpresa : IReglaNegocio
{
    public string Codigo => "R09";

    public string Descripcion => "Vuelve a preguntar la empresa cuando el postulante reaparece tras varios dias sin proceso vivo.";

    /// <summary>Antes de la 14 y la 1, que son las que enrutarian con el contexto viejo.</summary>
    public int Prioridad => 18;

    public bool Aplica(ContextoRegla ctx) =>
        ctx.Disparador == TipoDisparador.MensajeEntrante
        && ctx.Conversacion is { CuentaContextoId: not null }
        // FUN-09: «Otra empresa» es un pedido explicito de volver a empezar, sin esperar ningun plazo.
        && (ctx.PidioOtraEmpresa || ctx.DiasDesdeActividadAnterior is not null)
        // FUN-02: si el mensaje trae codigo o nombre de empresa, el postulante ya dijo por cual
        // escribe; repreguntarle seria ignorarlo.
        && ctx.OrigenEleccion is OrigenEleccion.Ninguna or OrigenEleccion.Contexto;

    public Task<ResultadoRegla> EvaluarAsync(ContextoRegla ctx, CancellationToken ct = default)
    {
        var dias = ctx.ConfigInt(ClavesConfiguracion.RepreguntaEmpresaDias, 3);

        // FUN-09: si pidio «Otra empresa» no hay plazo ni proceso vivo que respetar; lo que quiere es
        // justamente salir de lo que ya tiene.
        if (!ctx.PidioOtraEmpresa)
        {
            if (ctx.DiasDesdeActividadAnterior < dias)
                return Task.FromResult(ResultadoRegla.SinAccion);

            // Con un proceso vivo en la cuenta en contexto no hay nada que repreguntar: sigue con su
            // analista. Los procesos vivos en otras cuentas los resuelve la Regla 6 (FUN-09).
            if (ctx.CuentasVivas.Contains(ctx.Conversacion!.CuentaContextoId!.Value))
                return Task.FromResult(ResultadoRegla.SinAccion);
        }

        // No se manda la plantilla de reapertura: el mensaje del postulante acaba de abrir la
        // ventana de 24h, y el propio menu ya lleva su texto. Sumarla seria un segundo mensaje
        // sin informacion nueva.
        var motivo = ctx.PidioOtraEmpresa
            ? "El postulante pidio ver otras empresas."
            : $"El postulante volvio tras {ctx.DiasDesdeActividadAnterior:F0} dias sin actividad.";

        return Task.FromResult(ResultadoRegla.Detener(
            new LimpiarCuentaContexto(motivo),
            new ReiniciarIntentosMenu(),
            new MostrarMenuEmpresas(EsReintento: false),
            new RegistrarAuditoria("RepreguntaEmpresa", motivo)));
    }
}
