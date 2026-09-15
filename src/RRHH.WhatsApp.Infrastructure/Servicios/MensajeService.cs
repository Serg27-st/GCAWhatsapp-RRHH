using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Infrastructure.Servicios;

public sealed class MensajeService(RrhhDbContext db, TimeProvider reloj, ILogger<MensajeService> log) : IMensajeService
{
    public async Task<Mensaje?> RegistrarEntranteAsync(
        int conversacionId, MensajeEntranteDto dto, Guid correlationId, CancellationToken ct = default)
    {
        // Comprobacion previa: resuelve la enorme mayoria de los reintentos sin provocar una
        // excepcion de base de datos.
        var yaExiste = await db.Mensajes
            .AnyAsync(m => m.ProviderMessageId == dto.ProviderMessageId, ct);

        if (yaExiste)
        {
            log.LogInformation("Mensaje {ProviderMessageId} ya registrado. Se descarta el reintento.",
                dto.ProviderMessageId);
            return null;
        }

        var mensaje = new Mensaje
        {
            ConversacionId = conversacionId,
            ProviderMessageId = dto.ProviderMessageId,
            Direccion = DireccionMensaje.Entrante,
            Contenido = Recortar(dto.Contenido, 4096),
            FechaEnvio = dto.FechaUtc,
            // Un mensaje que nos llego ya esta entregado por definicion.
            EstadoEntrega = EstadoEntrega.Entregado,
            CorrelationId = correlationId
        };

        db.Mensajes.Add(mensaje);

        try
        {
            await db.SaveChangesAsync(ct);
            return mensaje;
        }
        catch (DbUpdateException ex) when (EsViolacionDeUnicidad(ex))
        {
            // Dos entregas simultaneas del mismo mensaje pueden pasar juntas la comprobacion
            // previa. El indice unico sobre ProviderMessageId es la garantia real; aca solo se
            // traduce a "ya estaba".
            db.Entry(mensaje).State = EntityState.Detached;

            log.LogInformation("Carrera detectada sobre {ProviderMessageId}. Se descarta el duplicado.",
                dto.ProviderMessageId);

            return null;
        }
    }

    public async Task<Mensaje> RegistrarSalienteAsync(
        int conversacionId, string contenido, int? plantillaId, int? analistaId,
        string? providerMessageId, Guid correlationId,
        IReadOnlyList<string>? parametrosPlantilla = null, CancellationToken ct = default)
    {
        var mensaje = new Mensaje
        {
            ConversacionId = conversacionId,
            ProviderMessageId = providerMessageId,
            Direccion = DireccionMensaje.Saliente,
            Contenido = Recortar(contenido, 4096),
            PlantillaId = plantillaId,
            ParametrosPlantillaJson = parametrosPlantilla is { Count: > 0 }
                ? JsonSerializer.Serialize(parametrosPlantilla)
                : null,
            AnalistaId = analistaId,
            FechaEnvio = reloj.GetUtcNow().UtcDateTime,
            EstadoEntrega = providerMessageId is null ? EstadoEntrega.Pendiente : EstadoEntrega.Enviado,
            CorrelationId = correlationId
        };

        db.Mensajes.Add(mensaje);
        await db.SaveChangesAsync(ct);

        return mensaje;
    }

    public async Task MarcarEnvioFallidoAsync(
        long mensajeId, string? error, ClaseFallo clase = ClaseFallo.Permanente,
        DateTime? proximoIntentoUtc = null, CancellationToken ct = default)
    {
        var mensaje = await db.Mensajes.FirstOrDefaultAsync(m => m.MensajeId == mensajeId, ct);

        if (mensaje is null)
            return;

        mensaje.EstadoEntrega = EstadoEntrega.Fallido;
        mensaje.ErrorProveedor = Recortar(error ?? "El proveedor rechazo el envio.", 500);
        mensaje.ClaseFallo = clase;
        mensaje.IntentosEnvio++;
        mensaje.ProximoIntentoUtc = proximoIntentoUtc;

        log.LogWarning(
            "Mensaje {MensajeId} fallido ({Clase}, intento {Intento}): {Error}. Proximo intento: {Proximo}",
            mensajeId, clase, mensaje.IntentosEnvio, mensaje.ErrorProveedor,
            proximoIntentoUtc?.ToString("O") ?? "ninguno");

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Mensaje>> ListarPendientesDeReintentoAsync(
        int maximo, DateTime ahoraUtc, CancellationToken ct = default) =>
        await db.Mensajes
            .Include(m => m.Conversacion)
            .Include(m => m.Plantilla)
            .Where(m => m.EstadoEntrega == EstadoEntrega.Fallido
                     && m.ClaseFallo == ClaseFallo.Transitorio
                     && m.ProximoIntentoUtc != null
                     && m.ProximoIntentoUtc <= ahoraUtc)
            .OrderBy(m => m.ProximoIntentoUtc)
            .Take(maximo)
            .ToListAsync(ct);

    public async Task MarcarEnvioLogradoAsync(
        long mensajeId, string? providerMessageId, CancellationToken ct = default)
    {
        var mensaje = await db.Mensajes.FirstOrDefaultAsync(m => m.MensajeId == mensajeId, ct);

        if (mensaje is null)
            return;

        mensaje.ProviderMessageId = providerMessageId;
        mensaje.EstadoEntrega = EstadoEntrega.Enviado;
        mensaje.ErrorProveedor = null;
        mensaje.ClaseFallo = ClaseFallo.Ninguno;
        mensaje.IntentosEnvio++;

        // Sin proximo intento: a partir de aca el estado lo mueven los acuses del proveedor.
        mensaje.ProximoIntentoUtc = null;

        await db.SaveChangesAsync(ct);
    }

    public async Task ActualizarEstadoEntregaAsync(EstadoEntregaDto dto, CancellationToken ct = default)
    {
        var mensaje = await db.Mensajes
            .FirstOrDefaultAsync(m => m.ProviderMessageId == dto.ProviderMessageId, ct);

        // Meta puede acusar mensajes que este sistema no envio (por ejemplo, enviados desde el
        // panel de 360dialog). No es un error: no hay nada que actualizar.
        if (mensaje is null)
            return;

        var nuevo = TraducirEstado(dto.Estado);

        // Los acuses llegan fuera de orden: un "sent" tardio no debe pisar un "read" ya recibido.
        if (nuevo <= mensaje.EstadoEntrega && nuevo != EstadoEntrega.Fallido)
            return;

        mensaje.EstadoEntrega = nuevo;

        if (nuevo == EstadoEntrega.Fallido)
        {
            mensaje.ErrorProveedor = Recortar(
                $"{dto.CodigoError} {dto.DescripcionError}".Trim(), 500);

            log.LogWarning("Envio fallido {ProviderMessageId}: {Error}",
                dto.ProviderMessageId, mensaje.ErrorProveedor);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Mensaje>> ListarPorConversacionAsync(
        int conversacionId, int maximo = 100, CancellationToken ct = default) =>
        await db.Mensajes
            .Where(m => m.ConversacionId == conversacionId)
            .OrderByDescending(m => m.FechaEnvio)
            .Take(maximo)
            .OrderBy(m => m.FechaEnvio)
            .ToListAsync(ct);

    /// <summary>El orden del enum refleja el avance del acuse, y es lo que permite ignorar los atrasados.</summary>
    private static EstadoEntrega TraducirEstado(string estado) => estado.ToLowerInvariant() switch
    {
        "sent" => EstadoEntrega.Enviado,
        "delivered" => EstadoEntrega.Entregado,
        "read" => EstadoEntrega.Leido,
        "failed" => EstadoEntrega.Fallido,
        _ => EstadoEntrega.Pendiente
    };

    private static string Recortar(string texto, int maximo) =>
        texto.Length <= maximo ? texto : texto[..maximo];

    /// <summary>2627 y 2601 son los errores de SQL Server para violacion de restriccion e indice unicos.</summary>
    private static bool EsViolacionDeUnicidad(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.SqlClient.SqlException sql
        && sql.Number is 2627 or 2601;
}
