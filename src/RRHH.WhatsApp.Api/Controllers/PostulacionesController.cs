using Microsoft.AspNetCore.Mvc;
using RRHH.WhatsApp.Api.Seguridad;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Contracts.Bandeja;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Api.Controllers;

/// <summary>
/// Lo que se hace sobre una postulacion, que es lo que recorre el kanban (Regla 13).
/// <para>
/// El dossier ponia mover la etapa bajo <c>/conversaciones/{id}</c>, pero lo que se mueve es la
/// postulacion: el hilo es por telefono y el tablero por vacante (V1). Con aquella ruta el tablero
/// mandaba el id de la postulacion donde se esperaba el de una conversacion, y ningun control de la
/// Regla 4 podia validar el objeto correcto (V22).
/// </para>
/// </summary>
[ApiController]
[Route("postulaciones")]
public sealed class PostulacionesController(
    IPostulacionService postulaciones,
    ICuentaService cuentas,
    AccionesBandeja acciones) : ControllerBase
{
    /// <summary>Regla 13, con la Regla 4 por cuenta: la mueven el titular y el respaldo de la cuenta.</summary>
    [HttpPost("{id:int}/etapa")]
    public async Task<IActionResult> MoverEtapa(
        int id, [FromBody] PeticionMoverEtapa peticion, CancellationToken ct)
    {
        var postulacion = await postulaciones.ObtenerPorIdAsync(id, ct);

        var nivel = postulacion is null
            ? NivelAcceso.Ninguno
            : await cuentas.ObtenerAccesoAsync(postulacion.CuentaId, User.AnalistaId(), ct);

        if (ResultadoAcceso.Evaluar(nivel, actua: true, "la postulación") is { } rechazo)
            return rechazo;

        try
        {
            await acciones.MoverEtapaAsync(id, peticion.EtapaId, User.AnalistaId(), ct);

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { motivo = ex.Message });
        }
    }
}
