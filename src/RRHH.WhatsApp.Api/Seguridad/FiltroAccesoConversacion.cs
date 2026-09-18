using Microsoft.AspNetCore.Mvc.Filters;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Api.Seguridad;

/// <summary>
/// Regla 4 en la frontera HTTP: toda accion de conversaciones que recibe un <c>id</c> pasa por aca
/// antes de ejecutarse.
/// <para>
/// Va como filtro del controlador y no como chequeo dentro de cada accion por la misma razon que la
/// politica de autorizacion de respaldo (V20): una accion nueva queda protegida sin que nadie tenga
/// que acordarse. Olvidarse de proteger un endpoint deberia ser imposible, no un hallazgo de revision.
/// </para>
/// </summary>
public sealed class FiltroAccesoConversacion(IConversacionService conversaciones) : IAsyncActionFilter
{
    /// <summary>Donde queda el nivel en <c>HttpContext.Items</c>, para que la accion no lo recalcule.</summary>
    public const string ClaveNivel = "Regla4.NivelAcceso";

    public async Task OnActionExecutionAsync(ActionExecutingContext contexto, ActionExecutionDelegate siguiente)
    {
        // Las listas (la bandeja, sin clasificar, el buscador) no llevan id: filtran en su consulta.
        if (!contexto.ActionArguments.TryGetValue("id", out var valor) || valor is not int conversacionId)
        {
            await siguiente();
            return;
        }

        var http = contexto.HttpContext;

        var nivel = await conversaciones.ObtenerAccesoAsync(
            conversacionId, http.User.AnalistaId(), http.RequestAborted);

        // FUN-01: tomar un hilo de la bandeja general se hace con nivel Lectura, porque todavia no es
        // de quien lo toma. El servicio valida lo que importa: que trabaje esa cuenta.
        var actua = ResultadoAcceso.Actua(http.Request) && !PermiteTomar(contexto);

        if (ResultadoAcceso.Evaluar(nivel, actua, "la conversación") is { } rechazo)
        {
            contexto.Result = rechazo;
            return;
        }

        http.Items[ClaveNivel] = nivel;

        await siguiente();
    }

    private static bool PermiteTomar(ActionExecutingContext contexto) =>
        contexto.ActionDescriptor.EndpointMetadata.OfType<PermiteTomarAttribute>().Any();
}
