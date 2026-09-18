using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Infrastructure.Servicios;

/// <summary>
/// Dueno de las respuestas del formulario y de los CVs. Concentra las dos validaciones que el
/// dossier pide explicitas y por separado: que la vacante siga abierta (Regla 20) y que el
/// postulante haya aceptado el aviso de privacidad (Regla 17).
/// </summary>
public sealed class JobFormsService(
    RrhhDbContext db,
    IAlmacenamientoCv almacenamiento,
    IConfiguracionReglasService configuracion,
    TimeProvider reloj,
    ILogger<JobFormsService> log) : IJobFormsService
{
    public async Task<JobFormsRespuesta> ValidarEnvioAsync(
        JobFormsRespuesta respuesta, CancellationToken ct = default)
    {
        if (!await ValidarVacanteActivaAsync(respuesta.HcId, ct))
        {
            throw new InvalidOperationException(
                $"La vacante {respuesta.HcId} ya no admite postulaciones.");
        }

        if (!await ValidarConsentimientoAsync(respuesta, ct))
        {
            throw new InvalidOperationException(
                "No se puede guardar la respuesta sin el consentimiento del aviso de privacidad.");
        }

        var config = await configuracion.ObtenerTodasAsync(ct);

        // Se sella la version vigente al momento del envio. Con Google Forms no se puede saber
        // cual vio exactamente el postulante; eso queda resuelto al migrar a Razor Pages
        // (ver docs/decisiones.md, riesgo de la Regla 17).
        respuesta.VersionAvisoPrivacidad = config.TryGetValue(
            ClavesConfiguracion.VersionAvisoPrivacidad, out var version) ? version : null;

        // Un solo instante para ambas fechas: el consentimiento y el envio pasan en la misma
        // operacion, y no hay razon para que difieran.
        var ahora = reloj.GetUtcNow().UtcDateTime;

        respuesta.FechaConsentimiento = ahora;
        respuesta.FechaEnvio = ahora;

        db.JobFormsRespuestas.Add(respuesta);

        await db.SaveChangesAsync(ct);

        return respuesta;
    }

    public Task<string> AlmacenarCvAsync(
        Stream contenido, string nombreArchivo, string contentType, CancellationToken ct = default) =>
        almacenamiento.GuardarAsync(contenido, nombreArchivo, contentType, ct);

    /// <summary>
    /// Regla 20. Una vacante cerrada desactiva su enlace: el bot debe avisarlo y volver a mostrar
    /// el menu, en vez de dejar que el postulante llene un formulario que no lleva a ninguna parte.
    /// </summary>
    public Task<bool> ValidarVacanteActivaAsync(int hcId, CancellationToken ct = default) =>
        db.Hcs.AsNoTracking().AnyAsync(h => h.HcId == hcId && h.Estado == EstadoHc.Abierta, ct);

    /// <summary>
    /// Regla 17. Sin consentimiento no se guarda el DNI ni el CV: es lo que respalda al sistema si
    /// el postulante pide despues la eliminacion de sus datos.
    /// </summary>
    public Task<bool> ValidarConsentimientoAsync(
        JobFormsRespuesta respuesta, CancellationToken ct = default) =>
        Task.FromResult(respuesta.ConsentimientoAceptado);

    /// <summary>
    /// Regla 17: respuestas cuyo CV supero el plazo de retencion. Solo trae las que todavia tienen
    /// archivo, para que la purga no vuelva a mirar lo ya limpiado.
    /// </summary>
    public async Task<IReadOnlyList<JobFormsRespuesta>> ListarCvsPorPurgarAsync(
        int diasRetencion, int maximo, CancellationToken ct = default)
    {
        var limite = reloj.GetUtcNow().UtcDateTime.AddDays(-diasRetencion);

        // A5 (FUN-16): el plazo corre desde la ultima actividad de la persona, no desde que mando el
        // formulario. Borrar el CV de alguien que sigue en un proceso —o que volvio la semana pasada—
        // seria perder el dato justo cuando hace falta.
        return await db.JobFormsRespuestas
            .AsNoTracking()
            .Where(r => r.CvUrl != null
                     && r.FechaEnvio <= limite
                     && !db.Postulaciones.Any(p =>
                            p.PostulanteId == r.PostulanteId
                            && (p.Estado == EstadoPostulacion.EnProceso
                                || p.Estado == EstadoPostulacion.Reingreso
                                || p.Estado == EstadoPostulacion.Contratado
                                || p.FechaUltimaActividad > limite)))
            .OrderBy(r => r.FechaEnvio)
            .Take(maximo)
            .ToListAsync(ct);
    }

    public async Task PurgarCvAsync(int respuestaId, CancellationToken ct = default)
    {
        var respuesta = await db.JobFormsRespuestas.FirstOrDefaultAsync(r => r.RespuestaId == respuestaId, ct);

        if (respuesta?.CvUrl is not { } ruta)
            return;

        // Un CV en Google Drive es del formulario, no nuestro: el archivo hay que borrarlo alla.
        // Se limpia igual la referencia, para que el dato personal no siga en nuestra base.
        if (ruta.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            log.LogWarning(
                "El CV de la respuesta {RespuestaId} vive en {Ruta} y debe eliminarse en el origen.",
                respuestaId, ruta);
        }
        else
        {
            await almacenamiento.EliminarAsync(ruta, ct);
        }

        respuesta.CvUrl = null;

        db.Auditorias.Add(new Auditoria
        {
            EntidadTipo = nameof(JobFormsRespuesta),
            EntidadId = respuestaId.ToString(),
            Accion = "PurgaCv",
            Detalle = "Retencion cumplida (Regla 17).",
            Fecha = reloj.GetUtcNow().UtcDateTime
        });

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// La respuesta ya guardada de esa invitacion. Nula si todavia no llego ninguna: es lo que
    /// distingue un envio nuevo de un reintento (V27).
    /// </summary>
    public Task<JobFormsRespuesta?> ObtenerRespuestaDeInvitacionAsync(
        int invitacionId, CancellationToken ct = default) =>
        db.JobFormsRespuestas
            .AsNoTracking()
            .OrderByDescending(r => r.RespuestaId)
            .FirstOrDefaultAsync(r => r.InvitacionId == invitacionId, ct);

    /// <summary>
    /// COR-15/M8: delega en el almacenamiento, sin tocar ninguna fila. Quien llama ya sabe que el
    /// CV no quedo asociado a ninguna respuesta -- si lo estuviera, correspondería
    /// <see cref="PurgarCvAsync"/>, que ademas limpia la referencia y audita.
    /// </summary>
    public Task EliminarCvAsync(string ruta, CancellationToken ct = default) =>
        almacenamiento.EliminarAsync(ruta, ct);
}
