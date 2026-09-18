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

    public async Task<MensajeAdjunto> RegistrarAdjuntoAsync(
        long mensajeId, MedioEntranteDto medio, CancellationToken ct = default)
    {
        var adjunto = new MensajeAdjunto
        {
            MensajeId = mensajeId,
            TipoMedio = Recortar(medio.Tipo, 20),
            ProveedorMedioId = Recortar(medio.ProveedorMedioId, 150),
            MimeType = Recortar(medio.MimeType, 100),
            // El nombre lo pone la persona: se guarda como vino, recortado a la columna. Nunca se usa
            // como ruta; el almacenamiento arma la suya (V33).
            NombreArchivo = medio.NombreArchivo is { } nombre ? Recortar(nombre, 255) : null,
            Estado = EstadoAdjunto.Pendiente,
            FechaRecepcion = reloj.GetUtcNow().UtcDateTime
        };

        db.MensajesAdjuntos.Add(adjunto);
        await db.SaveChangesAsync(ct);

        return adjunto;
    }

    public async Task<IReadOnlyList<MensajeAdjunto>> ListarAdjuntosPendientesAsync(
        DateTime ahoraUtc, int maximo, CancellationToken ct = default) =>
        await db.MensajesAdjuntos
            .AsNoTracking()
            .Where(a => a.Estado == EstadoAdjunto.Pendiente
                     && (a.ProximoIntentoUtc == null || a.ProximoIntentoUtc <= ahoraUtc))
            .OrderBy(a => a.FechaRecepcion)
            .ThenBy(a => a.AdjuntoId)
            .Take(maximo)
            .ToListAsync(ct);

    public async Task<bool> MarcarAdjuntoDescargadoAsync(
        long adjuntoId, string ruta, long tamanoBytes, CancellationToken ct = default)
    {
        var adjunto = await db.MensajesAdjuntos.FirstOrDefaultAsync(a => a.AdjuntoId == adjuntoId, ct);

        if (adjunto is null)
            return false;

        adjunto.Estado = EstadoAdjunto.Descargado;
        adjunto.Ruta = ruta;
        adjunto.TamanoBytes = tamanoBytes;
        adjunto.Error = null;
        adjunto.IntentosDescarga++;
        adjunto.ProximoIntentoUtc = null;

        await db.SaveChangesAsync(ct);

        return true;
    }

    public async Task RegistrarFalloDescargaAsync(
        long adjuntoId, string error, DateTime? proximoIntentoUtc, CancellationToken ct = default)
    {
        var adjunto = await db.MensajesAdjuntos.FirstOrDefaultAsync(a => a.AdjuntoId == adjuntoId, ct);

        if (adjunto is null)
            return;

        adjunto.IntentosDescarga++;
        adjunto.Error = Recortar(error, 500);
        adjunto.ProximoIntentoUtc = proximoIntentoUtc;

        // Sin proximo intento no hay nada mas que hacer con el: queda rechazado y la bandeja no lo ofrece.
        if (proximoIntentoUtc is null)
            adjunto.Estado = EstadoAdjunto.Rechazado;

        log.LogWarning(
            "Adjunto {AdjuntoId}: fallo el intento {Intento} ({Error}). Proximo intento: {Proximo}",
            adjuntoId, adjunto.IntentosDescarga, adjunto.Error, proximoIntentoUtc?.ToString("O") ?? "ninguno");

        await db.SaveChangesAsync(ct);
    }

    public Task<MensajeAdjunto?> ObtenerAdjuntoAsync(long adjuntoId, int conversacionId, CancellationToken ct = default) =>
        db.MensajesAdjuntos
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.AdjuntoId == adjuntoId && a.Mensaje!.ConversacionId == conversacionId, ct);

    public async Task<IReadOnlyList<MensajeAdjunto>> ListarAdjuntosPorPurgarAsync(
        int diasRetencion, int maximo, CancellationToken ct = default)
    {
        var limite = reloj.GetUtcNow().UtcDateTime.AddDays(-diasRetencion);

        // A5 (V33): el plazo corre desde la ultima actividad de la persona, no desde que llego el
        // archivo. Un CV mandado por WhatsApp no puede vencer antes que uno del formulario, ni borrarse
        // mientras la persona sigue en un proceso o ya fue contratada.
        return await db.MensajesAdjuntos
            .AsNoTracking()
            .Where(a => a.Estado == EstadoAdjunto.Descargado
                     && a.Mensaje!.Conversacion!.FechaUltimaActividad <= limite
                     && (a.Mensaje.Conversacion.PostulanteId == null
                         || !db.Postulaciones.Any(p =>
                                p.PostulanteId == a.Mensaje.Conversacion.PostulanteId
                                && (p.Estado == EstadoPostulacion.EnProceso
                                    || p.Estado == EstadoPostulacion.Reingreso
                                    || p.Estado == EstadoPostulacion.Contratado
                                    || p.FechaUltimaActividad > limite))))
            .OrderBy(a => a.FechaRecepcion)
            .ThenBy(a => a.AdjuntoId)
            .Take(maximo)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<string>> AnonimizarPorConversacionAsync(
        IReadOnlyCollection<int> conversacionIds, CancellationToken ct = default)
    {
        if (conversacionIds.Count == 0)
            return [];

        var mensajes = await db.Mensajes
            .Include(m => m.Adjuntos)
            .Where(m => conversacionIds.Contains(m.ConversacionId))
            .ToListAsync(ct);

        var rutas = new List<string>();

        foreach (var mensaje in mensajes)
        {
            // El texto es el dato personal del mensaje; los parametros de la plantilla llevan el nombre
            // y las opciones, lo que se le ofrecio. La fila queda: sostiene las metricas de la Regla 18.
            mensaje.Contenido = "[anonimizado]";
            mensaje.ParametrosPlantillaJson = null;
            mensaje.OpcionesJson = null;

            foreach (var adjunto in mensaje.Adjuntos)
            {
                if (adjunto.Ruta is { } ruta)
                    rutas.Add(ruta);

                adjunto.Estado = EstadoAdjunto.Purgado;
                adjunto.Ruta = null;

                // «CV Maria Quispe.pdf» tambien dice quien es: el nombre lo puso la persona.
                adjunto.NombreArchivo = null;
            }
        }

        await db.SaveChangesAsync(ct);

        return rutas;
    }

    public async Task MarcarAdjuntoPurgadoAsync(long adjuntoId, CancellationToken ct = default)
    {
        var adjunto = await db.MensajesAdjuntos.FirstOrDefaultAsync(a => a.AdjuntoId == adjuntoId, ct);

        if (adjunto is null || adjunto.Estado == EstadoAdjunto.Purgado)
            return;

        adjunto.Estado = EstadoAdjunto.Purgado;
        adjunto.Ruta = null;

        db.Auditorias.Add(new Auditoria
        {
            EntidadTipo = nameof(MensajeAdjunto),
            EntidadId = adjuntoId.ToString(),
            Accion = "PurgaAdjunto",
            Detalle = "Retencion cumplida (Regla 17).",
            Fecha = reloj.GetUtcNow().UtcDateTime
        });

        await db.SaveChangesAsync(ct);
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

    public async Task<ResultadoAcuse?> ActualizarEstadoEntregaAsync(EstadoEntregaDto dto, CancellationToken ct = default)
    {
        var mensaje = await db.Mensajes
            .Include(m => m.Conversacion)
            .FirstOrDefaultAsync(m => m.ProviderMessageId == dto.ProviderMessageId, ct);

        // Meta puede acusar mensajes que este sistema no envio (por ejemplo, enviados desde el
        // panel de 360dialog). No es un error: no hay nada que actualizar.
        if (mensaje is null)
            return null;

        // FUN-13: lo que mando el bot no tiene autor, y el que tiene que enterarse es quien atiende.
        var avisarA = mensaje.AnalistaId ?? mensaje.Conversacion?.AnalistaAtendiendoId;
        var nuevo = TraducirEstado(dto.Estado);

        // Los acuses llegan fuera de orden: un "sent" tardio no debe pisar un "read" ya recibido.
        // Se compara por orden de avance y no por el valor del enum: EnCola y Enviando se agregaron
        // despues (V29) con numeros mas altos que Enviado.
        if (OrdenAvance(nuevo) <= OrdenAvance(mensaje.EstadoEntrega) && nuevo != EstadoEntrega.Fallido)
            return new ResultadoAcuse(mensaje.MensajeId, mensaje.ConversacionId, avisarA, PasoAFallido: false);

        // P4: Meta reentrega el webhook. El acuse se vuelve a aplicar —puede traer un detalle mejor—,
        // pero solo la primera vez cuenta como transicion y dispara el aviso.
        var pasoAFallido = nuevo == EstadoEntrega.Fallido && mensaje.EstadoEntrega != EstadoEntrega.Fallido;

        mensaje.EstadoEntrega = nuevo;

        if (nuevo == EstadoEntrega.Fallido)
        {
            mensaje.ErrorProveedor = Recortar(
                $"{dto.CodigoError} {dto.DescripcionError}".Trim(), 500);

            // Meta ya lo acepto y despues lo dio por no entregado: no lo va a reintentar, y
            // reenviarlo no lo duplica. Es lo que permite ofrecer «Reintentar» en la bandeja.
            mensaje.ClaseFallo = ClaseFallo.Permanente;
            mensaje.ProximoIntentoUtc = null;

            log.LogWarning("Envio fallido {ProviderMessageId}: {Error}",
                dto.ProviderMessageId, mensaje.ErrorProveedor);
        }

        await db.SaveChangesAsync(ct);

        return new ResultadoAcuse(mensaje.MensajeId, mensaje.ConversacionId, avisarA, pasoAFallido);
    }

    public async Task<IReadOnlyList<Mensaje>> ListarPorConversacionAsync(
        int conversacionId, int maximo = 100, CancellationToken ct = default) =>
        await db.Mensajes
            // FUN-14: el chat muestra los archivos de cada mensaje.
            .Include(m => m.Adjuntos)
            .Where(m => m.ConversacionId == conversacionId)
            .OrderByDescending(m => m.FechaEnvio)
            .Take(maximo)
            .OrderBy(m => m.FechaEnvio)
            .ToListAsync(ct);

    public async Task<Mensaje?> EncolarSalienteAsync(
        int conversacionId, SalienteEncolado saliente, string claveIdempotencia, int? analistaId,
        Guid correlationId, bool reservadoParaEnvio = false, CancellationToken ct = default)
    {
        // Comprobacion previa: resuelve el reproceso normal sin provocar una excepcion de base.
        if (await db.Mensajes.AnyAsync(m => m.ClaveIdempotencia == claveIdempotencia, ct))
            return null;

        var mensaje = new Mensaje
        {
            ConversacionId = conversacionId,
            Direccion = DireccionMensaje.Saliente,
            Contenido = Recortar(saliente.Contenido, 4096),
            PlantillaId = saliente.PlantillaId,
            ParametrosPlantillaJson = saliente.Parametros is { Count: > 0 }
                ? JsonSerializer.Serialize(saliente.Parametros)
                : null,
            TipoSaliente = saliente.Tipo,
            OpcionesJson = saliente.Opciones is { Count: > 0 }
                ? JsonSerializer.Serialize(new OpcionesSaliente(saliente.Opciones, saliente.TextoBotonLista))
                : null,
            AnalistaId = analistaId,
            FechaEnvio = reloj.GetUtcNow().UtcDateTime,
            EstadoEntrega = reservadoParaEnvio ? EstadoEntrega.Enviando : EstadoEntrega.EnCola,
            FechaTomaEnvio = reservadoParaEnvio ? reloj.GetUtcNow().UtcDateTime : null,
            ClaveIdempotencia = claveIdempotencia,
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
            // Dos decisiones simultaneas del mismo envio pasaron juntas la comprobacion previa. El
            // indice unico es la garantia real; aca solo se traduce a "ya estaba encolado".
            db.Entry(mensaje).State = EntityState.Detached;

            log.LogInformation("El envio {Clave} ya estaba encolado. No se duplica.", claveIdempotencia);

            return null;
        }
    }

    public Task<Mensaje?> ObtenerPorClaveIdempotenciaAsync(string claveIdempotencia, CancellationToken ct = default) =>
        db.Mensajes.AsNoTracking().FirstOrDefaultAsync(m => m.ClaveIdempotencia == claveIdempotencia, ct);

    public async Task<IReadOnlyList<Mensaje>> TomarLoteEnColaAsync(int maximo, CancellationToken ct = default)
    {
        // Sin reserva por fila: hay un solo Worker activo (V24) y la respuesta del analista nace
        // Enviando, asi que nadie mas toma filas EnCola.
        var lote = await db.Mensajes
            .Include(m => m.Conversacion)
            .Include(m => m.Plantilla)
            .Where(m => m.EstadoEntrega == EstadoEntrega.EnCola)
            .OrderBy(m => m.FechaEnvio)
            .ThenBy(m => m.MensajeId)
            .Take(maximo)
            .ToListAsync(ct);

        if (lote.Count == 0)
            return lote;

        var ahora = reloj.GetUtcNow().UtcDateTime;

        foreach (var mensaje in lote)
        {
            mensaje.EstadoEntrega = EstadoEntrega.Enviando;

            // Desde aca cuenta el tiempo de espera de RecuperarEnviandoVencidosAsync.
            mensaje.ProximoIntentoUtc = null;
            mensaje.FechaTomaEnvio = ahora;
        }

        await db.SaveChangesAsync(ct);

        return lote;
    }

    public async Task<int> RecuperarEnviandoVencidosAsync(TimeSpan antiguedad, CancellationToken ct = default)
    {
        var limite = reloj.GetUtcNow().UtcDateTime - antiguedad;

        var vencidos = await db.Mensajes
            .Where(m => m.EstadoEntrega == EstadoEntrega.Enviando
                     && (m.FechaTomaEnvio ?? m.FechaEnvio) < limite)
            .ToListAsync(ct);

        foreach (var mensaje in vencidos)
        {
            // Pudo haber salido: el proceso murio entre llamar al proveedor y guardar el resultado.
            // Reenviarlo arriesga duplicarlo, asi que queda para que lo mire una persona.
            mensaje.EstadoEntrega = EstadoEntrega.Fallido;
            mensaje.ClaseFallo = ClaseFallo.Ambiguo;
            mensaje.ErrorProveedor = "Se perdio el resultado del envio: pudo haber salido o no.";
            mensaje.ProximoIntentoUtc = null;

            log.LogError(
                "Mensaje {MensajeId} quedo Enviando sin resultado: se marca ambiguo y no se reintenta.",
                mensaje.MensajeId);
        }

        if (vencidos.Count > 0)
            await db.SaveChangesAsync(ct);

        return vencidos.Count;
    }

    /// <summary>
    /// Orden en que avanza un saliente. Fallido no tiene lugar: un acuse "failed" siempre se aplica.
    /// </summary>
    private static int OrdenAvance(EstadoEntrega estado) => estado switch
    {
        EstadoEntrega.EnCola => 0,
        EstadoEntrega.Enviando => 1,
        EstadoEntrega.Pendiente => 2,
        EstadoEntrega.Enviado => 3,
        EstadoEntrega.Entregado => 4,
        EstadoEntrega.Leido => 5,
        _ => -1
    };

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
