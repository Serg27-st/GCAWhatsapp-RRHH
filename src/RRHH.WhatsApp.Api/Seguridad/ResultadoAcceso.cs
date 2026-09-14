using Microsoft.AspNetCore.Mvc;
using RRHH.WhatsApp.Domain.Enums;

namespace RRHH.WhatsApp.Api.Seguridad;

/// <summary>
/// Traduce un <see cref="NivelAcceso"/> a la respuesta HTTP que corresponde (Regla 4). Esta en un
/// solo lugar para que conversaciones, postulaciones y tableros rechacen de la misma forma.
/// </summary>
public static class ResultadoAcceso
{
    /// <summary>
    /// Nulo si se puede seguir. 404 cuando no lo ve: para quien pregunta no existe, y un 403 le
    /// confirmaria que el id es real. 403 cuando lo ve pero lo que intenta es actuar.
    /// </summary>
    public static IActionResult? Evaluar(NivelAcceso nivel, bool actua, string recurso) => nivel switch
    {
        NivelAcceso.Ninguno =>
            new NotFoundObjectResult(new { motivo = $"No existe {recurso} o no está a tu cargo." }),

        NivelAcceso.Lectura when actua =>
            new ObjectResult(new { motivo = $"Podés ver {recurso}, pero la trabaja otro analista." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            },

        _ => null
    };

    /// <summary>GET y HEAD miran; cualquier otro metodo actua.</summary>
    public static bool Actua(HttpRequest peticion) =>
        !HttpMethods.IsGet(peticion.Method) && !HttpMethods.IsHead(peticion.Method);
}
