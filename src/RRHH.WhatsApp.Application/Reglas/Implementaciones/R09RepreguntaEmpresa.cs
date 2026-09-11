using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas.Implementaciones;

/// <summary>
/// Regla 9 — repregunta de empresa tras inactividad.
/// <para>
/// Si el postulante vuelve a escribir despues de los dias configurados, el contexto de cuenta
/// anterior deja de ser confiable: pudo haber quedado de otra postulacion. El bot vuelve a
/// preguntar la empresa, salvo que el analista ya haya decidido sobre el caso.
/// </para>
/// <para>
/// Detiene la evaluacion a proposito: dejar correr a la Regla 1 volveria a enrutar la conversacion
/// hacia la cuenta que se acaba de descartar por vieja.
/// </para>
/// </summary>
public sealed class R09RepreguntaEmpresa : IReglaNegocio
{
    public string Codigo => "R09";

    public string Descripcion => "Vuelve a preguntar la empresa cuando el postulante reaparece tras varios dias.";

    /// <summary>Antes de la 14 y la 1, que son las que enrutarian con el contexto viejo.</summary>
    public int Prioridad => 18;

    public bool Aplica(ContextoRegla ctx) =>
        ctx.Disparador == TipoDisparador.MensajeEntrante
        && ctx.Conversacion is { CuentaContextoId: not null }
        && ctx.DiasDesdeMensajeAnterior is not null;

    public Task<ResultadoRegla> EvaluarAsync(ContextoRegla ctx, CancellationToken ct = default)
    {
        var dias = ctx.ConfigInt(ClavesConfiguracion.RepreguntaEmpresaDias, 3);

        if (ctx.DiasDesdeMensajeAnterior < dias)
            return Task.FromResult(ResultadoRegla.SinAccion);

        // El mini-mantenimiento del analista manda sobre la repregunta: si ya marco al postulante
        // como contratado o descartado, volver a ofrecerle el menu contradice esa decision.
        var decidido = ctx.EstadosPostulaciones.Any(
            e => e is EstadoPostulacion.Contratado or EstadoPostulacion.Descartado);

        if (decidido)
            return Task.FromResult(ResultadoRegla.SinAccion);

        // No se manda la plantilla de reapertura: el mensaje del postulante acaba de abrir la
        // ventana de 24h, y el propio menu ya lleva su texto. Sumarla seria un segundo mensaje
        // sin informacion nueva.
        return Task.FromResult(ResultadoRegla.Detener(
            new LimpiarCuentaContexto(
                $"El postulante volvio tras {ctx.DiasDesdeMensajeAnterior:F0} dias sin escribir."),
            new MostrarMenuEmpresas(EsReintento: false),
            new RegistrarAuditoria(
                "RepreguntaEmpresa",
                $"Contexto de cuenta descartado tras {ctx.DiasDesdeMensajeAnterior:F0} dias de inactividad.")));
    }
}
