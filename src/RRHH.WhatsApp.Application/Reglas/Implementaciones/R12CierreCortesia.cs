using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas.Implementaciones;

/// <summary>
/// Regla 12 — Mensaje de cierre automatizado (FUN-10, A11).
/// <para>
/// Cuando el postulante queda descartado, el sistema manda solo el mensaje de cortesia, sin que el
/// analista tenga que redactarlo cada vez. Sale una sola vez por postulacion: el sello
/// <c>FechaCierreCortesia</c> es lo que lo garantiza aunque la tarjeta se mueva varias veces (P4).
/// </para>
/// <para>
/// Fuera del horario de atencion no sale: despedir a alguien a las once de la noche es peor que
/// hacerlo al dia siguiente. Queda pedido y el barrido lo toma cuando la jornada abre.
/// </para>
/// </summary>
public sealed class R12CierreCortesia : IReglaNegocio
{
    public string Codigo => "R12";

    public string Descripcion => "Manda el mensaje de cierre de cortesia cuando la postulacion queda descartada.";

    /// <summary>Unica regla de su disparador; el numero solo la ordena frente a futuras.</summary>
    public int Prioridad => 60;

    public bool Aplica(ContextoRegla ctx) =>
        ctx.Disparador is TipoDisparador.CambioEstadoPostulacion or TipoDisparador.TiempoTranscurridoPostulacion
        && ctx.Conversacion is not null
        && ctx.Postulacion is { CierreCortesiaPendiente: true, FechaCierreCortesia: null };

    public Task<ResultadoRegla> EvaluarAsync(ContextoRegla ctx, CancellationToken ct = default)
    {
        // Sin horario no se manda: el barrido vuelve a mirarlo cuando la jornada abra.
        if (!ctx.DentroDeHorario)
            return Task.FromResult(ResultadoRegla.SinAccion);

        var postulacion = ctx.Postulacion!;
        var vacante = ctx.Hc?.Titulo ?? postulacion.Hc?.Titulo ?? "la vacante";
        var nombre = ctx.Postulante?.NombreCompleto;

        return Task.FromResult(ResultadoRegla.Con(
            // COR-03 (P1): con la ventana abierta sale en texto; el caso habitual —descarte dias
            // despues del ultimo mensaje— necesita la plantilla aprobada.
            new EnviarMensajeBot(
                TextosBot.CierreCortesia(nombre, vacante),
                ClavesPlantilla.CierreCortesia,
                [nombre ?? "hola", vacante]),

            // Solo si el mensaje llego a encolarse: sellarlo igual lo daria por enviado y el
            // postulante nunca recibiria su despedida (COR-03).
            new SellarPostulacion(postulacion.PostulacionId, MarcaPostulacion.CierreCortesiaEnviado)
            {
                SoloSiSeEnvioAnterior = true
            },

            new RegistrarAuditoria(
                "CierreCortesia",
                $"Postulacion {postulacion.PostulacionId} descartada: se envio el cierre de cortesia.")));
    }
}
