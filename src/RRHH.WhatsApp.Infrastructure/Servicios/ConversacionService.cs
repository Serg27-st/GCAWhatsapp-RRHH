using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Excepciones;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Domain.Reglas;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Infrastructure.Servicios;

public sealed class ConversacionService(
    RrhhDbContext db,
    ICuentaService cuentas,
    IAusenciaService ausencias,
    IHorarioAtencionService horarios,
    IConfiguracionReglasService configuracion,
    TimeProvider reloj,
    ILogger<ConversacionService> log)
    : IConversacionService
{
    public async Task<Conversacion> ObtenerOCrearAsync(string telefonoE164, CancellationToken ct = default)
    {
        var existente = await db.Conversaciones
            .FirstOrDefaultAsync(c => c.TelefonoE164 == telefonoE164, ct);

        if (existente is not null)
            return existente;

        var ahora = reloj.GetUtcNow().UtcDateTime;

        // Nace sin postulante y sin cuenta: cuando alguien escribe por primera vez solo tenemos su
        // telefono. El DNI llega con el JobForms y la cuenta la resuelve el menu del bot, que es el
        // que la atiende hasta entonces (V30).
        var conversacion = new Conversacion
        {
            TelefonoE164 = telefonoE164,
            Estado = EstadoConversacion.EnMenuBot,
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

        // Regla 4: quien la atiende la trabaja.
        if (conversacion.AnalistaAtendiendoId == analistaId && conversacion.Estado != EstadoConversacion.EnMenuBot)
            return NivelAcceso.Total;

        var rol = await db.Analistas.AsNoTracking()
            .Where(a => a.AnalistaId == analistaId)
            .Select(a => (RolAnalista?)a.Rol)
            .FirstOrDefaultAsync(ct);

        // Sistemas ve todo para soporte y auditoria, pero ver no es atender: responder en un hilo
        // ajeno le hablaria al postulante en nombre de un analista que no lo sabe. Incluye lo que el
        // bot esta atendiendo, que ninguna bandeja muestra.
        if (rol == RolAnalista.Sistemas)
            return NivelAcceso.Lectura;

        // V30: «Sin clasificar» la ven todos los analistas, pero para actuar hay que tomarla (FUN-01).
        // Con acceso total para cualquiera, varios le escribian a la vez al mismo postulante (R11).
        if (conversacion.Estado == EstadoConversacion.PendienteClasificar && rol == RolAnalista.Analista)
            return NivelAcceso.Lectura;

        return NivelAcceso.Ninguno;
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

        // V30: con analista, el hilo sale del menu del bot o de «Sin clasificar». Se limpia lo que
        // median esas esperas: el contador del menu y el plazo de la bandeja general.
        if (conversacion.Estado is EstadoConversacion.EnMenuBot or EstadoConversacion.PendienteClasificar)
        {
            conversacion.Estado = EstadoConversacion.Activa;
            conversacion.FechaPendienteDesde = null;
            conversacion.IntentosMenuFallidos = 0;
            conversacion.FechaTextoNoReconocido = null;
        }

        db.Auditorias.Add(Auditar(nameof(Conversacion), conversacionId, analistaId, "Asignacion", motivo));

        await db.SaveChangesAsync(ct);
    }

    public async Task EscalarAsync(
        int conversacionId, int analistaEsperadoId, int analistaRespaldoId, string motivo,
        CancellationToken ct = default)
    {
        var conversacion = await db.Conversaciones.FirstAsync(c => c.ConversacionId == conversacionId, ct);

        // COR-13 (M4): entre que el barrido armo el contexto y llego hasta aca pudo pasar de todo.
        // Si el titular ya respondio, no hay nada que escalar; si el hilo cambio de manos —una
        // transferencia, otro escalamiento—, escalarlo ahora se lo quitaria a quien lo tiene.
        if (conversacion.FechaUltimaRespuestaAnalista is { } respuesta
            && conversacion.FechaUltimoMensajeEntrante is { } entrante
            && respuesta >= entrante)
        {
            log.LogInformation(
                "No se escala la conversacion {ConversacionId}: el analista ya habia respondido.",
                conversacionId);

            return;
        }

        if (conversacion.AnalistaAtendiendoId != analistaEsperadoId)
        {
            log.LogInformation(
                "No se escala la conversacion {ConversacionId}: la atiende {Actual} y no {Esperado}.",
                conversacionId, conversacion.AnalistaAtendiendoId, analistaEsperadoId);

            return;
        }

        conversacion.AnalistaAtendiendoId = analistaRespaldoId;
        conversacion.Estado = EstadoConversacion.Escalada;

        // FUN-05 (A9): desde aca corre el plazo del segundo nivel, que avisa a Jefatura si el
        // respaldo tampoco responde.
        conversacion.FechaEscalamiento = reloj.GetUtcNow().UtcDateTime;
        conversacion.FechaAvisoSegundoNivel = null;

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

        // FUN-07: pasarle el hilo a quien esta de vacaciones o con descanso medico es dejarlo
        // esperando a quien no va a responder, y recien lo resolveria el vencimiento (Regla 14).
        if (await ausencias.EstaAusenteAsync(analistaDestinoId, reloj.GetUtcNow().UtcDateTime, ct))
            throw new InvalidOperationException("El analista destino esta ausente: elegi a otro.");

        var conversacion = await db.Conversaciones.FirstAsync(c => c.ConversacionId == conversacionId, ct);

        // FUN-01: transferir lo que todavia no tomo nadie seria repartir trabajo sin dueño. Primero
        // se toma de la bandeja general y recien despues se puede pasar a otro.
        if (conversacion.Estado is EstadoConversacion.PendienteClasificar or EstadoConversacion.EnMenuBot)
            throw new InvalidOperationException(MotivosBandeja.TomarPrimero);

        // Regla 8: de uno en uno. Una transferencia pendiente bloquea otra hasta que se responda.
        var pendiente = await db.Transferencias.AnyAsync(
            t => t.ConversacionId == conversacionId && t.Estado == EstadoTransferencia.Pendiente, ct);

        if (pendiente)
            throw new InvalidOperationException("Ya hay una transferencia pendiente de respuesta para esta conversacion.");

        // Un solo instante para toda la operacion: la transferencia y el aviso que dispara nacen
        // juntos, y no tiene sentido que difieran por microsegundos.
        var ahora = reloj.GetUtcNow().UtcDateTime;

        var transferencia = new Transferencia
        {
            ConversacionId = conversacionId,
            AnalistaOrigenId = analistaOrigenId,
            AnalistaDestinoId = analistaDestinoId,
            Urgente = urgente,
            Comentario = comentario,
            Fecha = ahora,
            // Una transferencia urgente se aplica sola; el resto espera la aceptacion del destino.
            Estado = urgente ? EstadoTransferencia.Aceptada : EstadoTransferencia.Pendiente,

            // A1: la no urgente vence si nadie responde. En horas habiles: pedida un viernes a las
            // 17:00 vence el lunes, no el sabado, cuando el destino tampoco iba a poder responderla.
            FechaVencimiento = urgente
                ? null
                : await horarios.SumarMinutosHabilesAsync(
                    conversacion.CuentaContextoId,
                    ahora,
                    await HorasVencimientoAsync(ct) * 60,
                    ct)
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
            FechaCreacion = ahora
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (EsViolacionDeUnicidad(ex))
        {
            // Dos pedidos simultaneos pasaron juntos la comprobacion de arriba y el indice de una sola
            // pendiente por conversacion rechazo el segundo. Es el mismo caso de negocio, no un error.
            foreach (var entrada in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
                entrada.State = EntityState.Detached;

            if (urgente)
                await db.Entry(conversacion).ReloadAsync(ct);

            throw new InvalidOperationException("Ya hay una transferencia pendiente de respuesta para esta conversacion.", ex);
        }

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

        // Un solo instante para la respuesta y el aviso que dispara, misma razon que en TransferirAsync.
        var ahora = reloj.GetUtcNow().UtcDateTime;

        transferencia.Estado = aceptada ? EstadoTransferencia.Aceptada : EstadoTransferencia.Rechazada;
        transferencia.FechaRespuesta = ahora;

        // Si se rechaza, la conversacion se queda con quien la tenia: el origen decide a quien
        // mas derivarla, siempre de uno en uno.
        if (aceptada && transferencia.Conversacion is { } conversacion)
            conversacion.AnalistaAtendiendoId = analistaDestinoId;

        db.Auditorias.Add(Auditar(nameof(Transferencia), transferenciaId, analistaDestinoId,
            aceptada ? "TransferenciaAceptada" : "TransferenciaRechazada", null));

        // Regla 8: el origen tambien tiene que enterarse. Si la rechazaron, el hilo sigue siendo suyo
        // y le toca derivarlo a otro; sin este aviso no sabria que tiene que hacerlo, y el
        // postulante quedaria esperando entre dos analistas que creen que lo atiende el otro.
        // ARQ-13 (AL6): el aviso dice que conversacion mirar, no quien es el postulante. El nombre y el
        // telefono viven en las tablas, que se purgan (Regla 17); repetirlos en la outbox los dejaria
        // fuera de esa purga. La bandeja los muestra al abrir el hilo.
        var destino = transferencia.AnalistaDestino?.Nombre ?? "El analista destino";

        db.EventosSistema.Add(new EventoSistema
        {
            Tipo = "AnalistaNotificado",
            Payload = JsonSerializer.Serialize(new
            {
                AnalistaId = transferencia.AnalistaOrigenId,
                Mensaje = aceptada
                    ? $"{destino} acepto la conversacion que le transferiste."
                    : $"{destino} rechazo la transferencia: la conversacion sigue con vos.",
                transferencia.ConversacionId
            }),
            CorrelationId = Guid.NewGuid(),
            FechaCreacion = ahora
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


    public async Task RetirarTransferenciaAsync(
        int transferenciaId, int analistaOrigenId, CancellationToken ct = default)
    {
        var transferencia = await db.Transferencias
            .Include(t => t.AnalistaOrigen)
            .FirstOrDefaultAsync(t => t.TransferenciaId == transferenciaId, ct)
            ?? throw new KeyNotFoundException($"No existe la transferencia {transferenciaId}.");

        // FUN-07: retirar es arrepentirse de haberla ofrecido, asi que solo puede hacerlo quien la
        // envio. El destino tiene aceptar y rechazar; esto es la salida del otro lado.
        if (transferencia.AnalistaOrigenId != analistaOrigenId)
            throw new InvalidOperationException("Solo quien envio la transferencia puede retirarla.");

        if (transferencia.Estado != EstadoTransferencia.Pendiente)
            throw new InvalidOperationException("Esta transferencia ya fue respondida.");

        var ahora = reloj.GetUtcNow().UtcDateTime;

        transferencia.Estado = EstadoTransferencia.Retirada;
        transferencia.FechaRespuesta = ahora;

        db.Auditorias.Add(Auditar(nameof(Transferencia), transferenciaId, analistaOrigenId,
            "TransferenciaRetirada", $"Retirada antes de que {transferencia.AnalistaDestinoId} respondiera."));

        // El destino la tenia en su lista esperando respuesta: sin aviso, la vería desaparecer sin
        // saber por que.
        var origen = transferencia.AnalistaOrigen?.Nombre ?? "El analista que te la envio";

        db.EventosSistema.Add(new EventoSistema
        {
            Tipo = "AnalistaNotificado",
            Payload = JsonSerializer.Serialize(new
            {
                AnalistaId = transferencia.AnalistaDestinoId,
                Mensaje = $"{origen} retiro la transferencia que te habia enviado.",
                transferencia.ConversacionId
            }),
            CorrelationId = Guid.NewGuid(),
            FechaCreacion = ahora
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Transferencia>> ListarTransferenciasEnviadasPendientesAsync(
        int analistaOrigenId, CancellationToken ct = default) =>
        await db.Transferencias
            .AsNoTracking()
            .Include(t => t.AnalistaDestino)
            .Include(t => t.Conversacion)
                .ThenInclude(c => c!.Postulante)
            .Where(t => t.AnalistaOrigenId == analistaOrigenId
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

        // V30: vuelve al menu del bot mientras repregunta, no a «Sin clasificar»: el bot todavia la
        // esta atendiendo. El menu empieza de cero.
        conversacion.Estado = EstadoConversacion.EnMenuBot;
        conversacion.AnalistaAtendiendoId = null;
        conversacion.IntentosMenuFallidos = 0;
        conversacion.FechaTextoNoReconocido = null;

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
        conversacion.FechaUltimaActividad = reloj.GetUtcNow().UtcDateTime;

        db.Auditorias.Add(Auditar(nameof(Conversacion), conversacionId, null,
            "VinculoPostulante", $"Postulante {postulanteId} identificado por el formulario."));

        await db.SaveChangesAsync(ct);
    }

    public async Task RegistrarOptInAsync(int conversacionId, OrigenOptIn origen, CancellationToken ct = default)
    {
        var conversacion = await db.Conversaciones.FirstAsync(c => c.ConversacionId == conversacionId, ct);

        if (conversacion.FechaOptIn is not null)
            return;

        conversacion.FechaOptIn = reloj.GetUtcNow().UtcDateTime;
        conversacion.OrigenOptIn = origen;

        await db.SaveChangesAsync(ct);
    }

    public async Task RegistrarRespuestaAnalistaAsync(int conversacionId, DateTime fechaUtc, CancellationToken ct = default)
    {
        var conversacion = await db.Conversaciones.FirstAsync(c => c.ConversacionId == conversacionId, ct);

        conversacion.FechaUltimaRespuestaAnalista = fechaUtc;
        conversacion.FechaUltimaActividad = fechaUtc;

        // FUN-05: responder reinicia el plazo del segundo nivel. Sin esto, el aviso a Jefatura
        // quedaria sellado para siempre y el proximo escalamiento del hilo no lo volveria a emitir.
        conversacion.FechaAvisoSegundoNivel = null;

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

        // V30: lo que el bot atiende no es de ninguna bandeja, tampoco de la de Sistemas.
        var consulta = db.Conversaciones
            .Include(c => c.Postulante)
            .Include(c => c.CuentaContexto)
            .Where(c => c.Estado != EstadoConversacion.Archivada && c.Estado != EstadoConversacion.EnMenuBot);

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



    public async Task VencerTransferenciaAsync(int transferenciaId, CancellationToken ct = default)
    {
        var transferencia = await db.Transferencias
            .FirstOrDefaultAsync(t => t.TransferenciaId == transferenciaId, ct);

        // A1: vence solo lo que sigue esperando. Si el destino respondio entre el barrido y esto, su
        // respuesta manda: vencerla despues le sacaria el hilo a quien acaba de aceptarlo.
        if (transferencia is not { Estado: EstadoTransferencia.Pendiente })
        {
            log.LogInformation(
                "La transferencia {TransferenciaId} ya no estaba pendiente: no se vence.", transferenciaId);

            return;
        }

        var ahora = reloj.GetUtcNow().UtcDateTime;

        transferencia.Estado = EstadoTransferencia.Vencida;
        transferencia.FechaRespuesta = ahora;

        db.Auditorias.Add(Auditar(nameof(Transferencia), transferenciaId, transferencia.AnalistaOrigenId,
            "TransferenciaVencida",
            $"Sin respuesta de {transferencia.AnalistaDestinoId}: el hilo sigue con quien la envio."));

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<int>> ListarConversacionesConTransferenciaVencidaAsync(
        DateTime ahora, int maximo, CancellationToken ct = default) =>
        await db.Transferencias
            .AsNoTracking()
            // FUN-07 (A1): pendientes, no urgentes y con el plazo cumplido. Las urgentes no tienen
            // vencimiento porque se aplican sin esperar respuesta.
            .Where(t => t.Estado == EstadoTransferencia.Pendiente
                     && !t.Urgente
                     && t.FechaVencimiento != null
                     && t.FechaVencimiento <= ahora)
            .OrderBy(t => t.FechaVencimiento)
            .Take(maximo)
            .Select(t => t.ConversacionId)
            .Distinct()
            .ToListAsync(ct);

    public async Task<int> ReasignarCarteraAsync(
        int analistaId, int autorId, CancellationToken ct = default)
    {
        var ahora = reloj.GetUtcNow().UtcDateTime;

        var hilos = await db.Conversaciones
            .Where(c => c.AnalistaAtendiendoId == analistaId && c.Estado != EstadoConversacion.Archivada)
            .ToListAsync(ct);

        // La dotacion de todas las cuentas involucradas, de una vez: una bandeja puede tener decenas de
        // hilos y casi siempre son de las mismas cuentas.
        var cuentaIds = hilos.Select(c => c.CuentaContextoId).OfType<int>().Distinct().ToList();

        var dotaciones = await db.AnalistaCuentas
            .AsNoTracking()
            .Where(ac => cuentaIds.Contains(ac.CuentaId) && ac.AnalistaId != analistaId)
            .ToListAsync(ct);

        foreach (var hilo in hilos)
        {
            // El respaldo primero; si el que se va era el respaldo, no queda ninguno en la lista y
            // entonces vuelve al titular (FUN-19).
            var dotacion = dotaciones.Where(ac => ac.CuentaId == hilo.CuentaContextoId).ToList();

            var reemplazo = dotacion.FirstOrDefault(ac => ac.EsBackup)?.AnalistaId
                ?? dotacion.FirstOrDefault(ac => !ac.EsBackup)?.AnalistaId;

            if (reemplazo is { } nuevo)
            {
                hilo.AnalistaAtendiendoId = nuevo;

                // Un hilo escalado que cambia de manos vuelve a estar activo: el escalamiento era
                // contra quien ya no esta, y el plazo del segundo nivel no puede seguir corriendo.
                if (hilo.Estado == EstadoConversacion.Escalada)
                {
                    hilo.Estado = EstadoConversacion.Activa;
                    hilo.FechaEscalamiento = null;
                    hilo.FechaAvisoSegundoNivel = null;
                }
            }
            else
            {
                // Sin cuenta o sin nadie que la cubra: a la bandeja general, con su plazo corriendo
                // desde ahora (P3). Es preferible que lo tome cualquiera a dejarlo con quien no esta.
                hilo.AnalistaAtendiendoId = null;
                hilo.Estado = EstadoConversacion.PendienteClasificar;
                hilo.FechaPendienteDesde = ahora;
                hilo.FechaAvisoPendiente = null;
            }

            db.Auditorias.Add(Auditar(nameof(Conversacion), hilo.ConversacionId, autorId,
                "ReasignadaPorBaja",
                reemplazo is { } destino
                    ? $"Pasa del analista {analistaId} al {destino}."
                    : $"Sin reemplazo para el analista {analistaId}: queda sin clasificar."));
        }

        await ResolverTransferenciasDeAsync(analistaId, ahora, ct);

        await db.SaveChangesAsync(ct);

        return hilos.Count;
    }

    /// <summary>
    /// FUN-19: una transferencia pendiente de alguien que ya no esta deja el hilo en un limbo entre dos
    /// analistas (V21). La que esperaba su respuesta se rechaza; la que el ofrecio se retira.
    /// </summary>
    private async Task ResolverTransferenciasDeAsync(int analistaId, DateTime ahora, CancellationToken ct)
    {
        var pendientes = await db.Transferencias
            .Where(t => t.Estado == EstadoTransferencia.Pendiente
                     && (t.AnalistaDestinoId == analistaId || t.AnalistaOrigenId == analistaId))
            .ToListAsync(ct);

        foreach (var transferencia in pendientes)
        {
            var esDestino = transferencia.AnalistaDestinoId == analistaId;

            transferencia.Estado = esDestino ? EstadoTransferencia.Rechazada : EstadoTransferencia.Retirada;
            transferencia.FechaRespuesta = ahora;

            var avisarA = esDestino ? transferencia.AnalistaOrigenId : transferencia.AnalistaDestinoId;

            db.Auditorias.Add(Auditar(nameof(Transferencia), transferencia.TransferenciaId, analistaId,
                esDestino ? "TransferenciaRechazadaPorBaja" : "TransferenciaRetiradaPorBaja",
                $"El analista {analistaId} dejo de atender conversaciones."));

            db.EventosSistema.Add(new EventoSistema
            {
                Tipo = "AnalistaNotificado",
                Payload = JsonSerializer.Serialize(new
                {
                    AnalistaId = avisarA,
                    Mensaje = esDestino
                        ? "La transferencia que enviaste volvio a vos: el analista destino ya no atiende conversaciones."
                        : "La transferencia que esperabas se retiro: quien la envio ya no atiende conversaciones.",
                    transferencia.ConversacionId
                }),
                CorrelationId = Guid.NewGuid(),
                FechaCreacion = ahora
            });
        }
    }

    public async Task<IReadOnlyList<int>> AnonimizarPorPostulanteAsync(
        int postulanteId, CancellationToken ct = default)
    {
        var hilos = await db.Conversaciones.Where(c => c.PostulanteId == postulanteId).ToListAsync(ct);

        foreach (var hilo in hilos)
        {
            // FUN-16: el telefono es el dato personal del hilo y a la vez su clave de busqueda. Al
            // cambiarlo, si la persona vuelve a escribir nace una conversacion nueva, con opt-in nuevo:
            // es lo que corresponde despues de pedir la eliminacion de sus datos.
            hilo.TelefonoE164 = $"ANON-{hilo.ConversacionId}";
        }

        if (hilos.Count > 0)
            await db.SaveChangesAsync(ct);

        return [.. hilos.Select(c => c.ConversacionId)];
    }

    public async Task<int> ContarAsignadasPorAusenciaAsync(
        IReadOnlyCollection<int> cuentaIds, int titularId, DateTime desdeUtc, DateTime hastaUtc,
        CancellationToken ct = default)
    {
        if (cuentaIds.Count == 0)
            return 0;

        // La auditoria guarda el id de la conversacion como texto: se comparan asi para que la
        // consulta viaje entera a la base y no traiga las auditorias del periodo a memoria.
        var asignadas = db.Auditorias
            .Where(a => a.Accion == "AsignacionPorAusencia"
                     && a.EntidadTipo == nameof(Conversacion)
                     && a.Fecha >= desdeUtc
                     && a.Fecha <= hastaUtc)
            .Select(a => a.EntidadId);

        return await db.Conversaciones
            .AsNoTracking()
            .Where(c => c.CuentaContextoId != null
                     && cuentaIds.Contains(c.CuentaContextoId.Value)
                     && c.AnalistaAtendiendoId != titularId
                     && asignadas.Contains(c.ConversacionId.ToString()))
            .CountAsync(ct);
    }
    public async Task<IReadOnlyList<int>> ListarPendientesSegundoNivelAsync(
        int maximo, CancellationToken ct = default) =>
        await db.Conversaciones
            .AsNoTracking()
            // FUN-05: escaladas, todavia sin el aviso a Jefatura y con el postulante esperando. Si el
            // plazo habil ya vencio lo decide la regla, que es la que conoce el parametro y el horario.
            .Where(c => c.Estado == EstadoConversacion.Escalada
                     && c.FechaEscalamiento != null
                     && c.FechaAvisoSegundoNivel == null
                     && c.FechaUltimoMensajeEntrante != null
                     && (c.FechaUltimaRespuestaAnalista == null
                         || c.FechaUltimaRespuestaAnalista < c.FechaUltimoMensajeEntrante))
            .OrderBy(c => c.FechaEscalamiento)
            .Take(maximo)
            .Select(c => c.ConversacionId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<int>> ListarPendientesDerivacionMenuAsync(
        int maximo, CancellationToken ct = default) =>
        await db.Conversaciones
            .AsNoTracking()
            // FUN-06 (A12): en el menu del bot, con un texto que no reconocio y sin novedades desde
            // entonces. Cuanto silencio hace falta lo decide la regla, en horas habiles.
            .Where(c => c.Estado == EstadoConversacion.EnMenuBot && c.FechaTextoNoReconocido != null)
            .OrderBy(c => c.FechaTextoNoReconocido)
            .Take(maximo)
            .Select(c => c.ConversacionId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<int>> ListarPendientesAvisoClasificacionAsync(
        int maximo, CancellationToken ct = default) =>
        await db.Conversaciones
            .AsNoTracking()
            // FUN-06 (P3): en la bandeja general, sin que nadie las tome y sin el aviso a Jefatura.
            .Where(c => c.Estado == EstadoConversacion.PendienteClasificar
                     && c.FechaPendienteDesde != null
                     && c.FechaAvisoPendiente == null)
            .OrderBy(c => c.FechaPendienteDesde)
            .Take(maximo)
            .Select(c => c.ConversacionId)
            .ToListAsync(ct);
    public async Task<IReadOnlyList<int>> ListarPendientesArchivadoAsync(
        int diasSinActividad, int maximo, CancellationToken ct = default)
    {
        var limite = reloj.GetUtcNow().UtcDateTime.AddDays(-diasSinActividad);

        return await db.Conversaciones
            .AsNoTracking()
            .Where(c => c.Estado != EstadoConversacion.Archivada && c.FechaUltimaActividad <= limite)
            .OrderBy(c => c.FechaUltimaActividad)
            .Take(maximo)
            .Select(c => c.ConversacionId)
            .ToListAsync(ct);
    }



    public async Task<Conversacion> TomarAsync(
        int conversacionId, int analistaId, int cuentaId, CancellationToken ct = default)
    {
        var conversacion = await db.Conversaciones.FirstAsync(c => c.ConversacionId == conversacionId, ct);

        // P3: solo se toma lo que esta esperando a alguien. Si ya lo atiende una persona, tomarlo
        // seria quitarselo por la ventana, sin transferencia ni rastro.
        if (conversacion.Estado != EstadoConversacion.PendienteClasificar)
        {
            throw new InvalidOperationException(
                $"La conversacion {conversacionId} no esta en «Sin clasificar»: esta {conversacion.Estado}.");
        }

        // Regla 4: tomar es adjudicarse el hilo, asi que hace falta el mismo acceso que para atender
        // esa cuenta. Sin esto, cualquiera podria sacar de la bandeja general lo que no le toca.
        if (await cuentas.ObtenerAccesoAsync(cuentaId, analistaId, ct) != NivelAcceso.Total)
        {
            throw new UnauthorizedAccessException(
                $"El analista {analistaId} no trabaja la cuenta {cuentaId}.");
        }

        conversacion.CuentaContextoId = cuentaId;
        conversacion.AnalistaAtendiendoId = analistaId;
        conversacion.Estado = EstadoConversacion.Activa;

        // Deja de correr el plazo de la bandeja general y el contador del menu del bot (V30).
        conversacion.FechaPendienteDesde = null;
        conversacion.IntentosMenuFallidos = 0;
        conversacion.FechaTextoNoReconocido = null;

        db.Auditorias.Add(Auditar(nameof(Conversacion), conversacionId, analistaId,
            "TomadaDeBandejaGeneral", $"Tomada para la cuenta {cuentaId}."));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Dos analistas tomandola a la vez: gana quien guardo primero, y el otro tiene que ver
            // que ya no esta disponible en vez de creer que la tiene.
            db.ChangeTracker.Clear();

            throw new ConflictoConcurrenciaException(
                "Otro analista tomo esta conversacion primero.");
        }

        log.LogInformation(
            "El analista {AnalistaId} tomo la conversacion {ConversacionId} para la cuenta {CuentaId}.",
            analistaId, conversacionId, cuentaId);

        return conversacion;
    }
    public async Task SellarAsync(int conversacionId, MarcaConversacion marca, CancellationToken ct = default)
    {
        var conversacion = await db.Conversaciones.FirstAsync(c => c.ConversacionId == conversacionId, ct);
        var ahora = reloj.GetUtcNow().UtcDateTime;

        switch (marca)
        {
            case MarcaConversacion.AvisoFueraHorario:
                conversacion.FechaAvisoFueraHorario = ahora;
                break;

            case MarcaConversacion.AvisoSegundoNivel:
                conversacion.FechaAvisoSegundoNivel = ahora;
                break;

            case MarcaConversacion.AvisoPendiente:
                conversacion.FechaAvisoPendiente = ahora;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(marca), marca, "No hay sello para esa marca.");
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task RegistrarIntentoMenuAsync(
        int conversacionId, bool textoNoReconocido, CancellationToken ct = default)
    {
        var conversacion = await db.Conversaciones.FirstAsync(c => c.ConversacionId == conversacionId, ct);

        conversacion.IntentosMenuFallidos++;

        // A12: la fecha solo se sella cuando el postulante escribio algo que el bot no entendio. El
        // primer mensaje del hilo no es un fallo suyo, y desde el no corre el plazo para derivar.
        if (textoNoReconocido)
            conversacion.FechaTextoNoReconocido = reloj.GetUtcNow().UtcDateTime;

        await db.SaveChangesAsync(ct);
    }

    public async Task ReiniciarIntentosMenuAsync(int conversacionId, CancellationToken ct = default)
    {
        var conversacion = await db.Conversaciones.FirstAsync(c => c.ConversacionId == conversacionId, ct);

        conversacion.IntentosMenuFallidos = 0;
        conversacion.FechaTextoNoReconocido = null;

        await db.SaveChangesAsync(ct);
    }

    public async Task DerivarAPendientesAsync(
        int conversacionId, string motivo, CancellationToken ct = default)
    {
        var conversacion = await db.Conversaciones.FirstAsync(c => c.ConversacionId == conversacionId, ct);

        conversacion.Estado = EstadoConversacion.PendienteClasificar;

        // P3: ningun hilo sin dueño ni plazo. Desde aca corre el aviso a Jefatura (FUN-06).
        conversacion.FechaPendienteDesde = reloj.GetUtcNow().UtcDateTime;
        conversacion.FechaTextoNoReconocido = null;

        db.Auditorias.Add(Auditar(nameof(Conversacion), conversacionId, null, "DerivadaABandejaGeneral", motivo));

        await db.SaveChangesAsync(ct);

        log.LogInformation(
            "La conversacion {ConversacionId} paso a Sin clasificar: {Motivo}", conversacionId, motivo);
    }

    public async Task ReactivarAsync(
        int conversacionId, EstadoConversacion nuevo, CancellationToken ct = default)
    {
        var conversacion = await db.Conversaciones.FirstAsync(c => c.ConversacionId == conversacionId, ct);

        if (conversacion.Estado != EstadoConversacion.Archivada)
            return;

        conversacion.Estado = nuevo;

        db.Auditorias.Add(Auditar(nameof(Conversacion), conversacionId, null,
            "Reactivada", $"El postulante volvio a escribir; el hilo pasa a {nuevo}."));

        await db.SaveChangesAsync(ct);
    }

    public async Task TomarContextoDePostulacionAsync(
        int conversacionId, int postulacionId, CancellationToken ct = default)
    {
        var conversacion = await db.Conversaciones.FirstAsync(c => c.ConversacionId == conversacionId, ct);

        var postulacion = await db.Postulaciones
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PostulacionId == postulacionId, ct);

        if (postulacion is null)
        {
            log.LogError(
                "Se pidio tomar el contexto de la postulacion {PostulacionId}, que no existe.", postulacionId);

            return;
        }

        conversacion.CuentaContextoId = postulacion.CuentaId;

        // Regla 1: si la postulacion no tiene asignado —la creo el bot antes de que nadie la tomara—,
        // el dueño es el titular de la cuenta.
        conversacion.AnalistaAtendiendoId = postulacion.AnalistaAsignadoId
            ?? await db.AnalistaCuentas
                .Where(ac => ac.CuentaId == postulacion.CuentaId && !ac.EsBackup && ac.Analista!.Activo)
                .Select(ac => (int?)ac.AnalistaId)
                .FirstOrDefaultAsync(ct);

        // V30: con dueño, el hilo deja de estar en el menu del bot o en la bandeja general.
        if (conversacion.AnalistaAtendiendoId is not null
            && conversacion.Estado is EstadoConversacion.EnMenuBot or EstadoConversacion.PendienteClasificar)
        {
            conversacion.Estado = EstadoConversacion.Activa;
            conversacion.FechaPendienteDesde = null;
            conversacion.IntentosMenuFallidos = 0;
            conversacion.FechaTextoNoReconocido = null;
        }

        db.Auditorias.Add(Auditar(nameof(Conversacion), conversacionId, conversacion.AnalistaAtendiendoId,
            "ContextoDePostulacion", $"El hilo continua la postulacion {postulacionId}."));

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// A1: horas habiles hasta que vence una transferencia no urgente. Sale de la configuracion como
    /// el resto de los plazos, para poder ajustarla sin redeploy.
    /// </summary>
    private async Task<int> HorasVencimientoAsync(CancellationToken ct)
    {
        var config = await configuracion.ObtenerTodasAsync(ct);

        return config.TryGetValue(ClavesConfiguracion.TransferenciaHorasVencimiento, out var valor)
            && int.TryParse(valor, out var horas)
            && horas > 0
                ? horas
                : 2;
    }
    private Auditoria Auditar(string tipo, int id, int? analistaId, string accion, string? detalle) =>
        new()
        {
            EntidadTipo = tipo,
            EntidadId = id.ToString(),
            AnalistaId = analistaId,
            Accion = accion,
            Detalle = detalle,
            Fecha = reloj.GetUtcNow().UtcDateTime
        };
}
