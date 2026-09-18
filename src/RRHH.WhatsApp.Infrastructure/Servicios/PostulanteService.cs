using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Infrastructure.Servicios;

/// <summary>
/// La persona detras de las postulaciones. El DNI es la clave real (Regla 9): no cambia aunque la
/// persona cambie de celular, y es lo que permite reconocerla entre cuentas.
/// </summary>
public sealed class PostulanteService(
    RrhhDbContext db,
    IAlmacenamientoCv almacenamiento,
    IAlmacenamientoAdjuntos adjuntos,
    IConversacionService conversaciones,
    IMensajeService mensajes,
    IEventoSistemaService eventos,
    IAuditoriaService auditoria,
    IUnidadTrabajo unidad,
    TimeProvider reloj,
    ILogger<PostulanteService> log) : IPostulanteService
{
    public Task<Postulante?> BuscarPorDniAsync(string dni, CancellationToken ct = default) =>
        db.Postulantes.AsNoTracking().FirstOrDefaultAsync(p => p.Dni == dni, ct);

    /// <summary>
    /// Crea el postulante o actualiza el que ya existe con ese DNI. La misma persona puede
    /// postular a varias vacantes y no debe duplicarse: por eso el DNI y no el telefono.
    /// </summary>
    public async Task<Postulante> RegistrarDesdeFormularioAsync(
        DatosPostulanteFormulario datos, CancellationToken ct = default)
    {
        var postulante = await db.Postulantes.FirstOrDefaultAsync(p => p.Dni == datos.Dni, ct);

        if (postulante is null)
        {
            postulante = new Postulante
            {
                Dni = datos.Dni,
                NombreCompleto = datos.NombreCompleto,
                TelefonoUltimo = datos.TelefonoE164,
                Email = datos.Email,
                FechaRegistro = reloj.GetUtcNow().UtcDateTime
            };

            db.Postulantes.Add(postulante);
        }
        else
        {
            // Se refresca lo que pudo cambiar desde la ultima postulacion, sin pisar con nulos lo
            // que ya se sabia de la persona.
            postulante.NombreCompleto = datos.NombreCompleto ?? postulante.NombreCompleto;
            postulante.TelefonoUltimo = datos.TelefonoE164 ?? postulante.TelefonoUltimo;
            postulante.Email = datos.Email ?? postulante.Email;
        }

        await db.SaveChangesAsync(ct);

        return postulante;
    }

    public async Task<IReadOnlyList<Postulacion>> ObtenerHistorialAsync(string dni, CancellationToken ct = default)
    {
        var postulante = await db.Postulantes.AsNoTracking().FirstOrDefaultAsync(p => p.Dni == dni, ct);

        if (postulante is null)
            return [];

        // Historial completo a traves de todas las cuentas: es lo que el flujo pide mostrarle al
        // analista cuando el DNI ya existe (Seccion 6.1).
        return await db.Postulaciones
            .AsNoTracking()
            .Include(p => p.Cuenta)
            .Include(p => p.Hc)
            .Include(p => p.EtapaKanban)
            .Where(p => p.PostulanteId == postulante.PostulanteId)
            .OrderByDescending(p => p.FechaCreacion)
            .ToListAsync(ct);
    }

    /// <summary>
    /// FUN-16 (AL6): un pedido de eliminacion tiene que alcanzar todo lo que dice quien es la persona
    /// —su ficha, sus mensajes, sus archivos, el rastro en la outbox y el detalle de la auditoria—, y
    /// dejar lo que sostiene las metricas de la Regla 18.
    /// <para>
    /// Todo en una transaccion (V28): a medias quedaria una persona anonimizada con sus mensajes
    /// intactos, que es peor que no haber empezado. Los archivos se borran dentro, como la purga del CV:
    /// si algo se deshace, lo que queda es una fila que apunta a un archivo que ya no esta, y eso la
    /// purga lo tolera.
    /// </para>
    /// </summary>
    public async Task AnonimizarDatosAsync(string dni, string motivo, CancellationToken ct = default)
    {
        var postulante = await db.Postulantes.FirstOrDefaultAsync(p => p.Dni == dni, ct)
            // Sin el DNI en el mensaje: el controlador lo registra en el log, y un pedido de borrado
            // de la Regla 17 no deberia dejar el dato que se pidio borrar escrito en otro lado.
            ?? throw new InvalidOperationException("No existe un postulante con ese DNI.");

        var id = postulante.PostulanteId;

        await unidad.EjecutarAsync(async c =>
        {
            var respuestas = await db.JobFormsRespuestas.Where(r => r.PostulanteId == id).ToListAsync(c);

            foreach (var respuesta in respuestas)
            {
                if (respuesta.CvUrl is { } ruta)
                    await EliminarCvAsync(ruta, c);

                respuesta.CvUrl = null;

                // Los campos del formulario tambien son datos personales: quedan vacios, no borrados,
                // para no romper la fila que sostiene la trazabilidad del consentimiento.
                respuesta.DatosJson = "{}";
            }

            // El hilo, sus mensajes y sus archivos. Cada servicio toca sus tablas (V6).
            var hilos = await conversaciones.AnonimizarPorPostulanteAsync(id, c);

            foreach (var ruta in await mensajes.AnonimizarPorConversacionAsync(hilos, c))
                await adjuntos.EliminarAsync(ruta, c);

            await eventos.AnonimizarPorConversacionAsync(hilos, c);
            await auditoria.AnonimizarDetallesAsync(hilos, id, c);

            // Se anonimiza en vez de eliminar filas: borrar al postulante se llevaria por delante sus
            // postulaciones, y con ellas las metricas historicas de la Regla 18.
            postulante.Dni = $"ANON-{id}";
            postulante.NombreCompleto = null;
            postulante.TelefonoUltimo = null;
            postulante.Email = null;

            // Despues de vaciar los detalles: este registro es el rastro del pedido, y su motivo tiene
            // que quedar legible para quien audite.
            db.Auditorias.Add(new Auditoria
            {
                EntidadTipo = nameof(Postulante),
                EntidadId = id.ToString(),
                Accion = "AnonimizacionDatos",
                Detalle = motivo,
                Fecha = reloj.GetUtcNow().UtcDateTime
            });

            await db.SaveChangesAsync(c);
        }, ct);

        log.LogInformation(
            "Datos personales del postulante {PostulanteId} anonimizados: {Motivo}", id, motivo);
    }

    /// <summary>
    /// Los CVs que viven en Google Drive no se pueden borrar desde aca: el archivo es del
    /// formulario, no nuestro. Se registra para que quede constancia de que falta ese paso manual
    /// mientras el JobForms siga en Google Forms.
    /// </summary>
    private async Task EliminarCvAsync(string ruta, CancellationToken ct)
    {
        if (ruta.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            log.LogWarning(
                "El CV {Ruta} vive fuera del sistema y debe eliminarse a mano en el origen.", ruta);

            return;
        }

        await almacenamiento.EliminarAsync(ruta, ct);
    }
}
