using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas.Implementaciones;

/// <summary>
/// Regla 3 — Fuera de horario.
/// <para>
/// Se avisa el horario de atencion, pero el flujo sigue activo: la conversacion se asigna igual y
/// cualquier analista puede responder si lo desea. Por eso esta regla solo agrega un envio y nunca
/// detiene la evaluacion ni cambia el estado de la conversacion.
/// </para>
/// <para>
/// Un aviso por periodo fuera de horario, no uno por mensaje: repetirlo es justo el ruido que eleva
/// los reportes de spam de la Seccion 2.4. El periodo se mide contra el ultimo cierre de la jornada
/// (FUN-04). Antes se median 8 horas desde el ultimo entrante, asi que tres mensajes seguidos un
/// viernes a la noche no producian ningun aviso (C1).
/// </para>
/// </summary>
public sealed class R03FueraDeHorario : IReglaNegocio
{
    public string Codigo => "R03";

    public string Descripcion => "Informa el horario de atencion cuando el mensaje llega fuera de jornada.";

    /// <summary>Despues de asignar, para que el aviso salga junto con el enrutamiento ya resuelto.</summary>
    public int Prioridad => 50;

    public bool Aplica(ContextoRegla ctx) =>
        ctx.Disparador == TipoDisparador.MensajeEntrante
        && ctx.Conversacion is not null
        && !ctx.DentroDeHorario
        // Sin cierre anterior no hay periodo que delimitar —no hay horario cargado—, y avisar seria
        // decirle al postulante un horario que nadie configuro.
        && ctx.InicioPeriodoFueraHorario is { } inicio
        && (ctx.Conversacion.FechaAvisoFueraHorario is not { } avisado || avisado < inicio);

    public Task<ResultadoRegla> EvaluarAsync(ContextoRegla ctx, CancellationToken ct = default)
    {
        var horario = ctx.DescripcionHorario ?? "nuestro horario habitual de oficina";

        return Task.FromResult(ResultadoRegla.Con(
            // COR-03 (P1): el postulante acaba de escribir, asi que la ventana esta abierta y el aviso
            // sale en texto con la proxima apertura incluida; fuera de ella queda la plantilla, que
            // solo admite el horario como parametro.
            new EnviarMensajeBot(
                TextosBot.FueraDeHorario(horario, ctx.ProximaApertura),
                ClavesPlantilla.FueraDeHorario,
                [horario]),

            // Sellar sin haber enviado dejaria al postulante sin aviso en todo el periodo (COR-03).
            new SellarConversacion(MarcaConversacion.AvisoFueraHorario) { SoloSiSeEnvioAnterior = true }));
    }
}
