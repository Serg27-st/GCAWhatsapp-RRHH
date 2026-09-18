using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Application.Casos;

/// <summary>
/// Revalida la Regla 15 justo antes de que un saliente toque la red. Lo usan el despachador y el
/// reintento: entre que se decidio el envio y este momento pudo cerrarse la ventana de 24h, perderse
/// el opt-in o desactivarse la plantilla, y un mensaje que era legal al decidirlo ya no lo es (V29).
/// </summary>
public sealed class ValidadorEnvio(IConversacionService conversaciones, IPlantillaService plantillas)
{
    /// <summary>El motivo por el que no puede salir, o nulo si puede.</summary>
    public async Task<string?> MotivoParaNoEnviarAsync(Mensaje mensaje, CancellationToken ct = default)
    {
        var conversacion = mensaje.Conversacion
            ?? await conversaciones.ObtenerPorIdAsync(mensaje.ConversacionId, ct);

        if (conversacion is null)
            return "La conversacion ya no existe.";

        if (conversacion.FechaOptIn is null)
            return "Ya no hay opt-in registrado para este numero (Regla 15).";

        if (EsPlantilla(mensaje))
        {
            // Se relee de la base y no del mensaje: lo que importa es si esta activa ahora. Una
            // plantilla que se desactivo desde el primer intento es justo la que provoca sanciones.
            var plantilla = mensaje.PlantillaId is { } id ? await plantillas.ObtenerPorIdAsync(id, ct) : null;

            return plantilla is { Activa: true }
                ? null
                : "La plantilla no esta activa o aprobada en Meta.";
        }

        // Texto, botones y listas son mensajes de sesion: con la ventana cerrada, no.
        return await plantillas.ValidarVentana24hAsync(conversacion.ConversacionId, ct)
            ? null
            : "La ventana de 24h se cerro mientras el mensaje esperaba: enviarlo sin plantilla violaria la Regla 15.";
    }

    /// <summary>Los salientes previos a la cola no tienen tipo: se reconocen por la plantilla.</summary>
    public static bool EsPlantilla(Mensaje mensaje) =>
        mensaje.TipoSaliente == TipoSaliente.Plantilla || mensaje.PlantillaId is not null;
}
