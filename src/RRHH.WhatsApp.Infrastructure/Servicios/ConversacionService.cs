using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Infrastructure.Servicios;

public sealed class ConversacionService(RrhhDbContext db, ILogger<ConversacionService> log) : IConversacionService
{
    public async Task<Conversacion> ObtenerOCrearAsync(string telefonoE164, CancellationToken ct = default)
    {
        var existente = await db.Conversaciones
            .FirstOrDefaultAsync(c => c.TelefonoE164 == telefonoE164, ct);

        if (existente is not null)
            return existente;

        var ahora = DateTime.UtcNow;

        // Nace sin postulante y sin cuenta: cuando alguien escribe por primera vez solo tenemos su
        // telefono. El DNI llega con el JobForms y la cuenta la resuelve el menu del bot.
        var conversacion = new Conversacion
        {
            TelefonoE164 = telefonoE164,
            Estado = EstadoConversacion.PendienteClasificar,
            FechaCreacion = ahora,
            FechaUltimaActividad = ahora
        };

        db.Conversaciones.Add(conversacion);

        try
        {
            await db.SaveChangesAsync(ct);
            return conversacion;
        }
        catch (DbUpdateException ex) when (EsViolacionDeUnicidad(ex))
        {
            // Dos mensajes del mismo numero llegando a la vez: el indice unico sobre el telefono
            // deja pasar solo uno. El otro relee en vez de fallar.
            db.Entry(conversacion).State = EntityState.Detached;
            return await db.Conversaciones.FirstAsync(c => c.TelefonoE164 == telefonoE164, ct);
        }
    }

    /// <summary>2627 y 2601 son los errores de SQL Server para violacion de restriccion e indice unicos.</summary>
    private static bool EsViolacionDeUnicidad(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.SqlClient.SqlException sql
        && sql.Number is 2627 or 2601;

    public Task<Conversacion?> ObtenerPorIdAsync(int conversacionId, CancellationToken ct = default) =>
        db.Conversaciones
            .Include(c => c.Postulante)
            .Include(c => c.CuentaContexto)
            .FirstOrDefaultAsync(c => c.ConversacionId == conversacionId, ct);

    public async Task<NivelAcceso> ObtenerAccesoAsync(
        int conversacionId, int analistaId, CancellationToken ct = default)
    {
        var conversacion = await db.Conversaciones
            .AsNoTracking()
            .Where(c => c.ConversacionId == conversacionId)
            .Select(c => new { c.AnalistaAtendiendoId, c.Estado })
            .FirstOrDefaultAsync(ct);

        if (conversacion is null)
            return NivelAcceso.Ninguno;

        // Regla 4: quien la atiende la trabaja. Regla 19: lo que el bot no pudo clasificar es de
        // todos hasta que alguien lo tome, y para tomarlo hay que poder responder.
        if (conversacion.AnalistaAtendiendoId == analistaId
            || conversacion.Estado == EstadoConversacion.PendienteClasificar)
            return NivelAcceso.Total;

        // Sistemas ve todo para soporte y auditoria, pero ver no es atender: responder en un hilo
        // ajeno le hablaria al postulante en nombre de un analista que no lo sabe.
        return await EsSistemasAsync(analistaId, ct) ? NivelAcceso.Lectura : NivelAcceso.Ninguno;
    }

    private Task<bool> EsSistemasAsync(int analistaId, CancellationToken ct) =>
        db.Analistas.AnyAsync(a => a.AnalistaId == analistaId && a.Rol == RolAnalista.Sistemas, ct);

    public async Task RegistrarEntradaAsync(int conversacionId, DateTime fechaUtc, CancellationToken ct = default)
    {
        var conversacion = await db.Conversaciones.FirstAsync(c => c.ConversacionId == conversacionId, ct);

        // Solo un mensaje ENTRANTE mueve esta fecha: es la que abre la ventana de servicio de 24h.
        conversacion.FechaUltimoMensajeEntrante = fechaUtc;
        conversacion.FechaUltimaActividad = fechaUtc;

        // Regla 15: que el postulante escriba primero es consentimiento. Se sella una sola vez,
        // porque lo que importa es cuando se obtuvo, no el ultimo mensaje.
        if (conversacion.FechaOptIn is null)
        {
            conversacion.FechaOptIn = fechaUtc;
            conversacion.OrigenOptIn = OrigenOptIn.MensajeEntrante;

            log.LogInformation("Opt-in registrado para la conversacion {ConversacionId}.", conversacionId);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task AsignarAnalistaAsync(int conversacionId, int analistaId, string motivo, CancellationToken ct = default)
    {
        var conversacion = await db.Conversaciones.FirstAsync(c => c.ConversacionId == conversacionId, ct);

        conversacion.AnalistaAtendiendoId = analistaId;

        if (conversacion.Estado == EstadoConversacion.PendienteClasificar)
            conversacion.Estado = EstadoConversacion.Activa;

        db.Auditorias.Add(Auditar(nameof(Conversacion), conversacionId, analistaId, "Asignacion", motivo));

        await db.SaveChangesAsync(ct);
    }

    public async Task EscalarAsync(int conversacionId, int analistaRespaldoId, string motivo, CancellationToken ct = default)
    {
        var conversacion = await db.Conversaciones.FirstAsync(c => c.ConversacionId == conversacionId, ct);

        conversacion.AnalistaAtendiendoId = analistaRespaldoId;
        conversacion.Estado = EstadoConversacion.Escalada;

        db.Auditorias.Add(Auditar(nameof(Conversacion), conversacionId, analistaRespaldoId, "Escalamiento", motivo));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // El caso concreto: el Worker escala justo cuando el analista titular responde.
            // Si respondio, ya no corresponde escalar — gana el analista y el escalamiento se
            // descarta. Silenciarlo con un reintento ciego produciria un escalamiento indebido.
            log.LogInformation(
                "El escalamiento de la conversacion {ConversacionId} se descarto: la fila cambio mientras tanto.",
                conversacionId);

            db.ChangeTracker.Clear();
        }
    }

    public async Task<Transferencia> TransferirAsync(
        int conversacionId, int analistaOrigenId, int analistaDestinoId,
        bool urgente, string? comentario, CancellationToken ct = default)
    {
        if (analistaOrigenId == analistaDestinoId)
            throw new InvalidOperationException("No se puede transferir una conversacion al mismo analista.");

        // V23: Jefatura y Sistemas no atienden conversaciones. Pasarles una la dejaria con alguien
        // que no la va a responder, o que por la Regla 4 no deberia actuar sobre ella.
        var destinoAtiende = await db.Analistas.AnyAsync(
            a => a.AnalistaId == analistaDestinoId && a.Activo && a.Rol == RolAnalista.Analista, ct);

        if (!destinoAtiende)
            throw new InvalidOperationException("Solo se puede transferir a un analista activo que atienda conversaciones.");

        var conversacion = await db.Conversaciones.FirstAsync(c => c.ConversacionId == conversacionId, ct);

        // Regla 8: de uno en uno. Una transferencia pendiente bloquea otra hasta que se responda.
        var pendiente = await db.Transferencias.AnyAsync(
            t => t.ConversacionId == conversacionId && t.Estado == EstadoTransferencia.Pendiente, ct);

        if (pendiente)
            throw new InvalidOperationException("Ya hay una transferencia pendiente de respuesta para esta conversacion.");

        var transferencia = new Transferencia
        {
            ConversacionId = conversacionId,
            AnalistaOrigenId = analistaOrigenId,
            AnalistaDestinoId = analistaDestinoId,
            Urgente = urgente,
            Comentario = comentario,
            Fecha = DateTime.UtcNow,
            // Una transferencia urgente se aplica sola; el resto espera la aceptacion del destino.
            Estado = urgente ? EstadoTransferencia.Aceptada : EstadoTransferencia.Pendiente
        };

        if (urgente)
        {
            conversacion.AnalistaAtendiendoId = analistaDestinoId;
            transferencia.FechaRespuesta = transferencia.Fecha;
        }

        db.Transferencias.Add(transferencia);
        db.Auditorias.Add(Auditar(nameof(Conversacion), conversacionId, analistaOrigenId,
            urgente ? "TransferenciaUrgente" : "TransferenciaSolicitada",
            $"Destino {analistaDestinoId}. {comentario}".Trim()));

        // Regla 8: sin este aviso el destino no se entera de que le transfirieron algo, y una
        // transferencia que espera aceptacion se queda esperando para siempre. Va por la outbox
        // igual que el resto: el difusor de la Api lo empuja al hub cuando el analista este.
        db.EventosSistema.Add(new EventoSistema
        {
            Tipo = "AnalistaNotificado",
            Payload = JsonSerializer.Serialize(new
            {
                AnalistaId = analistaDestinoId,
                Mensaje = urgente
                    ? "Te transfirieron una conversacion como urgente."
                    : "Tenes una transferencia esperando tu respuesta.",
                ConversacionId = conversacionId
            }),
            CorrelationId = Guid.NewGuid(),
            FechaCreacion = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);

        return transferencia;
    }

    public async Task ResponderTransferenciaAsync(
        int transferenciaId, int analistaDestinoId, bool aceptada, CancellationToken ct = default)
    {
        // FirstOrDefault y no First: con First, un id inexistente propaga el "Sequence contains no
        // elements" de EF hasta la respuesta HTTP, que no le dice nada a quien llama.
        var transferencia = await db.Transferencias
            .Include(t => t.AnalistaDestino)
            .Include(t => t.Conversacion)
                .ThenInclude(c => c!.Postulante)
            .FirstOrDefaultAsync(t => t.TransferenciaId == transferenciaId, ct)
            ?? throw new KeyNotFoundException($"No existe la transferencia {transferenciaId}.");

        if (transferencia.AnalistaDestinoId != analistaDestinoId)
            throw new InvalidOperationException("Solo el analista destino puede responder esta transferencia.");

        if (transferencia.Estado != EstadoTransferencia.Pendiente)
            throw new InvalidOperationException("Esta transferencia ya fue respondida.");

        transferencia.Estado = aceptada ? EstadoTransferencia.Aceptada : EstadoTransferencia.Rechazada;
        transferencia.FechaRespuesta = DateTime.UtcNow;

        // Si se rechaza, la conversacion se queda con quien la tenia: el origen decide a quien
        // mas derivarla, siempre de uno en uno.
        if (aceptada && transferencia.Conversacion is { } conversacion)
            conversacion.AnalistaAtendiendoId = analistaDestinoId;

        db.Auditorias.Add(Auditar(nameof(Transferencia), transferenciaId, analistaDestinoId,
            aceptada ? "TransferenciaAceptada" : "TransferenciaRechazada", null));

        // Regla 8: el origen tambien tiene que enterarse. Si la rechazaron, el hilo sigue siendo suyo
        // y le toca derivarlo a otro; sin este aviso no sabria que tiene que hacerlo, y el
        // postulante quedaria esperando entre dos analistas que creen que lo atiende el otro.
        var destino = transferencia.AnalistaDestino?.Nombre ?? "El analista destino";
        var sobre = (transferencia.Conversacion?.Postulante?.NombreCompleto
            ?? transferencia.Conversacion?.TelefonoE164) is { } quien ? $" ({quien})" : string.Empty;

        db.EventosSistema.Add(new EventoSistema
        {
            Tipo = "AnalistaNotificado",
            Payload = JsonSerializer.Serialize(new
            {
                AnalistaId = transferencia.AnalistaOrigenId,
                Mensaje = aceptada
                    ? $"{destino} aceptó la conversación que le transferiste{sobre}."
                    : $"{destino} rechazó la transferencia{sobre}: la conversación sigue con vos.",
                transferencia.ConversacionId
            }),
            CorrelationId = Guid.NewGuid(),
            FechaCreacion = DateTime.UtcNow
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // RowVersion de Transferencia y de Conversacion (Seccion 9.6.2): otra respuesta, o el
            // Worker escalando el mismo hilo, llego primero. Es un rechazo de negocio y no un 500:
            // lo que corresponde es recargar y mirar el estado nuevo, no reintentar a ciegas.
            db.ChangeTracker.Clear();

            throw new InvalidOperationException(
                "La transferencia cambió mientras respondías. Recargá la bandeja y volvé a intentarlo.");
        }
    }

    public async Task<IReadOnlyList<Transferencia>> ListarTransferenciasPendientesAsync(
        int analistaDestinoId, CancellationToken ct = default) =>
        await db.Transferencias
            .AsNoTracking()
            .Include(t => t.AnalistaOrigen)
            .Include(t => t.Conversacion)
                .ThenInclude(c => c!.Postulante)
            .Include(t => t.Conversacion)
                .ThenInclude(c => c!.CuentaContexto)
            .Where(t => t.AnalistaDestinoId == analistaDestinoId
                     && t.Estado == EstadoTransferencia.Pendiente)
            .OrderBy(t => t.Fecha)
            .ToListAsync(ct);

    public async Task EstablecerCuentaContextoAsync(int conversacionId, int cuentaId, CancellationToken ct = default)
    {
        var conversacion = await db.Conversaciones.FirstAsync(c => c.ConversacionId == conversacionId, ct);

        conversacion.CuentaContextoId = cuentaId;
        await db.SaveChangesAsync(ct);
    }

    public async Task LimpiarCuentaContextoAsync(int conversacionId, string motivo, CancellationToken ct = default)
    {
        var conversacion = await db.Conversaciones.FirstAsync(c => c.ConversacionId == conversacionId, ct);

        conversacion.CuentaContextoId = null;

        // Vuelve a la bandeja general mientras el bot repregunta: sin cuenta identificada, la
        // conversacion no pertenece a nadie en particular (mismo destino que la Regla 19).
        conversacion.Estado = EstadoConversacion.PendienteClasificar;
        conversacion.AnalistaAtendiendoId = null;

        db.Auditorias.Add(Auditar(nameof(Conversacion), conversacionId, null, "RepreguntaEmpresa", motivo));

        await db.SaveChangesAsync(ct);
    }

    public async Task VincularPostulanteAsync(
        int conversacionId, int postulanteId, CancellationToken ct = default)
    {
        var conversacion = await db.Conversaciones.FirstAsync(c => c.ConversacionId == conversacionId, ct);

        if (conversacion.PostulanteId == postulanteId)
            return;

        conversacion.PostulanteId = postulanteId;
        conversacion.FechaUltimaActividad = DateTime.UtcNow;

        db.Auditorias.Add(Auditar(nameof(Conversacion), conversacionId, null,
            "VinculoPostulante", $"Postulante {postulanteId} identificado por el formulario."));

        await db.SaveChangesAsync(ct);
    }

    public async Task RegistrarOptInAsync(int conversacionId, OrigenOptIn origen, CancellationToken ct = default)
    {
        var conversacion = await db.Conversaciones.FirstAsync(c => c.ConversacionId == conversacionId, ct);

        if (conversacion.FechaOptIn is not null)
            return;

        conversacion.FechaOptIn = DateTime.UtcNow;
        conversacion.OrigenOptIn = origen;

        await db.SaveChangesAsync(ct);
    }

    public async Task RegistrarRespuestaAnalistaAsync(int conversacionId, DateTime fechaUtc, CancellationToken ct = default)
    {
        var conversacion = await db.Conversaciones.FirstAsync(c => c.ConversacionId == conversacionId, ct);

        conversacion.FechaUltimaRespuestaAnalista = fechaUtc;
        conversacion.FechaUltimaActividad = fechaUtc;

        await db.SaveChangesAsync(ct);
    }

    public async Task CambiarEstadoAsync(int conversacionId, EstadoConversacion estado, CancellationToken ct = default)
    {
        var conversacion = await db.Conversaciones.FirstAsync(c => c.ConversacionId == conversacionId, ct);

        conversacion.Estado = estado;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Conversacion>> ListarParaAnalistaAsync(int analistaId, CancellationToken ct = default)
    {
        var analista = await db.Analistas.FirstOrDefaultAsync(a => a.AnalistaId == analistaId, ct)
            ?? throw new InvalidOperationException($"No existe el analista {analistaId}.");

        var consulta = db.Conversaciones
            .Include(c => c.Postulante)
            .Include(c => c.CuentaContexto)
            .Where(c => c.Estado != EstadoConversacion.Archivada);

        // Regla 4: el analista ve solo lo suyo; Sistemas ve todo, para soporte y auditoria.
        if (analista.Rol != RolAnalista.Sistemas)
            consulta = consulta.Where(c => c.AnalistaAtendiendoId == analistaId);

        return await consulta
            .OrderByDescending(c => c.FechaUltimaActividad)
            .ToListAsync(ct);
    }


    public async Task<IReadOnlyList<Conversacion>> BuscarPorDniAsync(
        string dni, int analistaId, CancellationToken ct = default)
    {
        var consulta = db.Conversaciones
            .AsNoTracking()
            .Include(c => c.Postulante)
            .Include(c => c.CuentaContexto)
            .Where(c => c.Postulante != null && c.Postulante.Dni == dni);

        // Reglas 4 y 6: el buscador trae el chat propio; no es una puerta a las conversaciones de
        // otras cuentas. Mismo criterio que la bandeja: quien la atiende, o Sistemas.
        if (!await EsSistemasAsync(analistaId, ct))
            consulta = consulta.Where(c => c.AnalistaAtendiendoId == analistaId);

        return await consulta
            .OrderByDescending(c => c.FechaUltimaActividad)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Conversacion>> ListarPendientesClasificarAsync(CancellationToken ct = default) =>
        await db.Conversaciones
            .Include(c => c.Postulante)
            .Where(c => c.Estado == EstadoConversacion.PendienteClasificar)
            .OrderBy(c => c.FechaUltimaActividad)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<int>> ListarPendientesEscalamientoAsync(
        int maximo, CancellationToken ct = default) =>
        await db.Conversaciones
            .AsNoTracking()
            // Prefiltro barato para el barrido: hilo activo, con dueno, y con un mensaje del
            // postulante posterior a la ultima respuesta del analista. Si el plazo de la Regla 2
            // ya vencio lo resuelve la regla, que es la que conoce el parametro y el horario.
            .Where(c => c.Estado == EstadoConversacion.Activa
                     && c.AnalistaAtendiendoId != null
                     && c.FechaUltimoMensajeEntrante != null
                     && (c.FechaUltimaRespuestaAnalista == null
                         || c.FechaUltimaRespuestaAnalista < c.FechaUltimoMensajeEntrante))
            .OrderBy(c => c.FechaUltimoMensajeEntrante)
            .Take(maximo)
            .Select(c => c.ConversacionId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<int>> ListarPendientesArchivadoAsync(
        int diasSinActividad, int maximo, CancellationToken ct = default)
    {
        var limite = DateTime.UtcNow.AddDays(-diasSinActividad);

        return await db.Conversaciones
            .AsNoTracking()
            .Where(c => c.Estado != EstadoConversacion.Archivada && c.FechaUltimaActividad <= limite)
            .OrderBy(c => c.FechaUltimaActividad)
            .Take(maximo)
            .Select(c => c.ConversacionId)
            .ToListAsync(ct);
    }

    private static Auditoria Auditar(string tipo, int id, int? analistaId, string accion, string? detalle) =>
        new()
        {
            EntidadTipo = tipo,
            EntidadId = id.ToString(),
            AnalistaId = analistaId,
            Accion = accion,
            Detalle = detalle,
            Fecha = DateTime.UtcNow
        };
}
