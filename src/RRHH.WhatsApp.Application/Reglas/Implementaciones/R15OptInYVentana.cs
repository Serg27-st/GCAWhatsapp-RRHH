using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas.Implementaciones;

/// <summary>
/// Regla 15 — Limite de opt-in / anti-bloqueo.
/// <para>
/// Ningun analista puede escribirle primero a un numero que no inicio la conversacion, y fuera de
/// la ventana de servicio de 24h todo envio debe ir como plantilla aprobada por Meta. Es la regla
/// que ataca directamente la causa del bloqueo original (Seccion 2.4), asi que corre antes que
/// cualquier otra que pueda producir un envio y detiene la evaluacion cuando bloquea.
/// </para>
/// </summary>
public sealed class R15OptInYVentana : IReglaNegocio
{
    public string Codigo => "R15";

    public string Descripcion =>
        "Bloquea el envio saliente sin opt-in registrado y exige plantilla fuera de la ventana de 24h.";

    /// <summary>La mas alta del sistema: ninguna regla debe poder enviar antes de que esta valide.</summary>
    public int Prioridad => 10;

    public bool Aplica(ContextoRegla ctx) =>
        ctx.Disparador == TipoDisparador.EnvioSaliente && ctx.Conversacion is not null;

    public Task<ResultadoRegla> EvaluarAsync(ContextoRegla ctx, CancellationToken ct = default)
    {
        // Sin consentimiento no se escribe, ni siquiera con plantilla: es el caso que Meta
        // penaliza como mensaje no solicitado.
        if (!ctx.TieneOptIn)
        {
            return Task.FromResult(ResultadoRegla.Detener(
                new BloquearEnvio(
                    "No existe opt-in registrado para este numero: el postulante nunca escribio " +
                    "primero ni completo un JobForms.")));
        }

        // Con la ventana cerrada el envio sigue siendo posible, pero solo como plantilla aprobada.
        // Quien orquesta el envio consulta esta senal antes de armar el mensaje.
        if (!ctx.VentanaServicioAbierta)
        {
            return Task.FromResult(ResultadoRegla.Con(
                new PublicarEvento("EnvioRequierePlantilla", new
                {
                    ctx.Conversacion!.ConversacionId,
                    UltimoMensajeEntrante = ctx.Conversacion.FechaUltimoMensajeEntrante
                })));
        }

        return Task.FromResult(ResultadoRegla.SinAccion);
    }
}
