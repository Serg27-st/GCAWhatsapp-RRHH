using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Application.Reglas;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Domain.Reglas;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Infrastructure.Servicios;

public sealed class FabricaContextoRegla(
    RrhhDbContext db,
    IConfiguracionReglasService configuracion,
    IHorarioAtencionService horarios,
    IAusenciaService ausencias) : IFabricaContextoRegla
{
    public async Task<ContextoRegla> ParaMensajeEntranteAsync(
        int conversacionId, string? idBotonPulsado, Guid correlationId, CancellationToken ct = default)
    {
        var ahora = DateTime.UtcNow;
        var conversacion = await CargarConversacionAsync(conversacionId, ct);

        // La vacante se carga sin filtrar por estado: que este cerrada es informacion, no ausencia
        // de dato. Es lo que le permite a la Regla 20 decir que ya fue cubierta, en vez de tratar
        // el boton como si no existiera.
        var hc = await CargarVacanteAsync(IdsBoton.LeerVacante(idBotonPulsado), ct);

        // Si el postulante pulso un boton del menu, esa eleccion manda sobre el contexto anterior:
        // es como cambia de empresa sin abrir otra conversacion (Regla 6 sobre un hilo unico).
        // Un boton de vacante fija tambien la cuenta, porque la vacante ya la determina.
        var cuentaId = hc?.CuentaId
            ?? IdsBoton.LeerCuenta(idBotonPulsado)
            ?? conversacion.CuentaContextoId;

        return await ArmarAsync(
            TipoDisparador.MensajeEntrante, conversacion, cuentaId, hc, postulacion: null, ahora, correlationId, ct);
    }

    public async Task<ContextoRegla> ParaTiempoTranscurridoAsync(int conversacionId, CancellationToken ct = default)
    {
        var ahora = DateTime.UtcNow;
        var conversacion = await CargarConversacionAsync(conversacionId, ct);

        return await ArmarAsync(
            TipoDisparador.TiempoTranscurrido, conversacion, conversacion.CuentaContextoId,
            hc: null, postulacion: null, ahora, Guid.NewGuid(), ct);
    }

    public async Task<ContextoRegla> ParaEnvioSalienteAsync(
        int conversacionId, Guid correlationId, CancellationToken ct = default)
    {
        var ahora = DateTime.UtcNow;
        var conversacion = await CargarConversacionAsync(conversacionId, ct);

        return await ArmarAsync(
            TipoDisparador.EnvioSaliente, conversacion, conversacion.CuentaContextoId,
            hc: null, postulacion: null, ahora, correlationId, ct);
    }

    public async Task<ContextoRegla> ParaJobFormsCompletadoAsync(
        int conversacionId, int hcId, Guid correlationId, CancellationToken ct = default)
    {
        var ahora = DateTime.UtcNow;
        var conversacion = await CargarConversacionAsync(conversacionId, ct);
        var hc = await CargarVacanteAsync(hcId, ct);

        return await ArmarAsync(
            TipoDisparador.JobFormsCompletado, conversacion,
            hc?.CuentaId ?? conversacion.CuentaContextoId, hc, postulacion: null, ahora, correlationId, ct);
    }

    public async Task<ContextoRegla> ParaCambioEstadoPostulacionAsync(
        int postulacionId, Guid correlationId, CancellationToken ct = default)
    {
        var ahora = DateTime.UtcNow;

        var postulacion = await db.Postulaciones
            .AsNoTracking()
            .Include(p => p.Hc)
            .FirstAsync(p => p.PostulacionId == postulacionId, ct);

        // El hilo es por telefono y la postulacion por persona: se llega de una a otra por el
        // postulante, que a esta altura ya se conoce porque completo el formulario.
        var conversacion = await db.Conversaciones
            .Include(c => c.Postulante)
            .Where(c => c.PostulanteId == postulacion.PostulanteId)
            .OrderByDescending(c => c.FechaUltimaActividad)
            .FirstOrDefaultAsync(ct);

        if (conversacion is null)
        {
            throw new InvalidOperationException(
                $"La postulacion {postulacionId} no tiene un hilo de WhatsApp con el que responderle.");
        }

        return await ArmarAsync(
            TipoDisparador.CambioEstadoPostulacion, conversacion, postulacion.CuentaId,
            postulacion.Hc, postulacion, ahora, correlationId, ct);

    }
    private async Task<ContextoRegla> ArmarAsync(
        TipoDisparador disparador,
        Conversacion conversacion,
        int? cuentaId,
        Hc? hc,
        Postulacion? postulacion,
        DateTime ahora,
        Guid correlationId,
        CancellationToken ct)
    {
        var config = await configuracion.ObtenerTodasAsync(ct);

        var cuenta = cuentaId is { } id
            ? await db.Cuentas.AsNoTracking().FirstOrDefaultAsync(c => c.CuentaId == id && c.Activo, ct)
            : null;

        var (titular, respaldo) = await ResolverAnalistasAsync(cuenta?.CuentaId, ct);

        var titularAusente = titular is not null
            && await ausencias.EstaAusenteAsync(titular.AnalistaId, ahora, ct);

        var (reloj, habiles) = await CalcularEsperaAsync(conversacion, cuenta?.CuentaId, ahora, ct);

        // La invitacion se resuelve antes del contexto porque de ella sale tambien el enlace: la
        // Regla 9 lo necesita como parametro de la plantilla y armar URLs no es tarea suya.
        var invitacion = await InvitacionPendienteAsync(conversacion.ConversacionId, ct);

        return new ContextoRegla
        {
            Disparador = disparador,
            AhoraUtc = ahora,
            CorrelationId = correlationId,
            Conversacion = conversacion,
            Postulante = conversacion.Postulante,
            Postulacion = postulacion,
            Cuenta = cuenta,
            Hc = hc,
            AnalistaTitular = titular,
            AnalistaRespaldo = respaldo,
            TitularAusente = titularAusente,
            DentroDeHorario = await horarios.EstaEnHorarioAsync(cuenta?.CuentaId, ahora, ct),
            OtrasCuentasEnProceso = await OtrasCuentasAsync(conversacion.PostulanteId, cuenta?.CuentaId, ct),
            VacantesAbiertas = await VacantesAbiertasAsync(cuenta?.CuentaId, ct),
            HcsConInvitacion = await HcsConInvitacionAsync(conversacion.ConversacionId, ct),
            Invitacion = invitacion,
            EnlaceInvitacion = EnlaceJobForms.Construir(invitacion?.Hc?.UrlJobForms, invitacion?.Token ?? Guid.Empty),
            EstadosPostulaciones = await EstadosPostulacionesAsync(conversacion.PostulanteId, ct),
            DiasDesdeMensajeAnterior = await DiasDesdeMensajeAnteriorAsync(conversacion, ct),
            IntentosMenuFallidos = await ContarIntentosMenuAsync(conversacion, ct),
            MinutosSinRespuestaReloj = reloj,
            MinutosSinRespuestaHabiles = habiles,
            Configuracion = config
        };
    }

    private async Task<Hc?> CargarVacanteAsync(int? hcId, CancellationToken ct) =>
        hcId is { } id
            ? await db.Hcs.AsNoTracking().FirstOrDefaultAsync(h => h.HcId == id, ct)
            : null;

    /// <summary>Regla 20 y Regla 9: a que puede postular hoy quien eligio esta cuenta.</summary>
    private async Task<IReadOnlyList<Hc>> VacantesAbiertasAsync(int? cuentaId, CancellationToken ct)
    {
        if (cuentaId is not { } id)
            return [];

        return await db.Hcs
            .AsNoTracking()
            .Where(h => h.CuentaId == id && h.Estado == EstadoHc.Abierta)
            .OrderBy(h => h.Titulo)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Regla 9: vacantes de este hilo que ya recibieron enlace, completadas o no. Se miran todas y
    /// no solo las pendientes, porque reenviarle el formulario a quien ya lo completo es peor que
    /// no mandarlo.
    /// </summary>
    private async Task<IReadOnlyList<int>> HcsConInvitacionAsync(int conversacionId, CancellationToken ct) =>
        await db.JobFormsInvitaciones
            .AsNoTracking()
            .Where(i => i.ConversacionId == conversacionId)
            .Select(i => i.HcId)
            .Distinct()
            .ToListAsync(ct);

    /// <summary>Regla 16 y Regla 9: en que quedaron las postulaciones del mismo DNI, en toda cuenta.</summary>
    private async Task<IReadOnlyList<EstadoPostulacion>> EstadosPostulacionesAsync(
        int? postulanteId, CancellationToken ct)
    {
        if (postulanteId is not { } id)
            return [];

        return await db.Postulaciones
            .AsNoTracking()
            .Where(p => p.PostulanteId == id)
            .Select(p => p.Estado)
            .Distinct()
            .ToListAsync(ct);
    }

    /// <summary>
    /// Regla 9. El hueco se mide entre los dos ultimos mensajes ENTRANTES y no contra
    /// FechaUltimaActividad, porque el mensaje que disparo la evaluacion ya la movio al presente
    /// antes de que las reglas corran.
    /// </summary>
    private async Task<double?> DiasDesdeMensajeAnteriorAsync(
        Conversacion conversacion, CancellationToken ct)
    {
        var ultimosDos = await db.Mensajes
            .AsNoTracking()
            .Where(m => m.ConversacionId == conversacion.ConversacionId
                     && m.Direccion == DireccionMensaje.Entrante)
            .OrderByDescending(m => m.FechaEnvio)
            .Take(2)
            .Select(m => m.FechaEnvio)
            .ToListAsync(ct);

        // Un solo mensaje entrante es el primer contacto: no hay hueco anterior que medir.
        return ultimosDos.Count < 2 ? null : (ultimosDos[0] - ultimosDos[1]).TotalDays;
    }

    private Task<Conversacion> CargarConversacionAsync(int conversacionId, CancellationToken ct) =>
        db.Conversaciones
            .Include(c => c.Postulante)
            .FirstAsync(c => c.ConversacionId == conversacionId, ct);

    /// <summary>
    /// Regla 1 y Regla 2: el titular es el responsable de la cuenta y el respaldo es fijo, no
    /// aleatorio. Ambos salen de AnalistaCuenta, donde un indice unico garantiza un solo respaldo.
    /// </summary>
    private async Task<(Analista? Titular, Analista? Respaldo)> ResolverAnalistasAsync(
        int? cuentaId, CancellationToken ct)
    {
        if (cuentaId is not { } id)
            return (null, null);

        var asignaciones = await db.AnalistaCuentas
            .AsNoTracking()
            .Include(ac => ac.Analista)
            .Where(ac => ac.CuentaId == id && ac.Analista!.Activo)
            .ToListAsync(ct);

        return (
            asignaciones.FirstOrDefault(a => !a.EsBackup)?.Analista,
            asignaciones.FirstOrDefault(a => a.EsBackup)?.Analista);
    }

    /// <summary>Regla 6: otras cuentas donde el mismo DNI sigue en proceso, para el aviso generico.</summary>
    private async Task<IReadOnlyList<Cuenta>> OtrasCuentasAsync(
        int? postulanteId, int? cuentaActualId, CancellationToken ct)
    {
        if (postulanteId is not { } id)
            return [];

        return await db.Postulaciones
            .AsNoTracking()
            .Where(p => p.PostulanteId == id
                     && p.Estado == EstadoPostulacion.EnProceso
                     && p.CuentaId != cuentaActualId)
            .Select(p => p.Cuenta!)
            .Distinct()
            .ToListAsync(ct);
    }

    private Task<JobFormsInvitacion?> InvitacionPendienteAsync(int conversacionId, CancellationToken ct) =>
        db.JobFormsInvitaciones
            .AsNoTracking()
            .Include(i => i.Hc)
            .Where(i => i.ConversacionId == conversacionId && !i.Completado)
            .OrderByDescending(i => i.FechaEnvioLink)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Regla 19. Mientras el bot no identifique la cuenta, cada mensaje entrante cuenta como un
    /// intento; el primero es el contacto inicial y no un fallo, por eso se descuenta.
    /// </summary>
    private async Task<int> ContarIntentosMenuAsync(Conversacion conversacion, CancellationToken ct)
    {
        if (conversacion.CuentaContextoId is not null)
            return 0;

        var entrantes = await db.Mensajes
            .CountAsync(m => m.ConversacionId == conversacion.ConversacionId
                          && m.Direccion == DireccionMensaje.Entrante, ct);

        return Math.Max(0, entrantes - 1);
    }

    /// <summary>
    /// Cuanto lleva el postulante esperando respuesta. Nulo si el analista ya contesto el ultimo
    /// mensaje: en ese caso no hay nada que escalar.
    /// </summary>
    private async Task<(double? Reloj, double? Habiles)> CalcularEsperaAsync(
        Conversacion conversacion, int? cuentaId, DateTime ahora, CancellationToken ct)
    {
        if (conversacion.FechaUltimoMensajeEntrante is not { } ultimoEntrante)
            return (null, null);

        if (conversacion.FechaUltimaRespuestaAnalista is { } respuesta && respuesta >= ultimoEntrante)
            return (null, null);

        var reloj = (ahora - ultimoEntrante).TotalMinutes;
        var habiles = await horarios.MinutosHabilesEntreAsync(cuentaId, ultimoEntrante, ahora, ct);

        return (reloj, habiles);
    }
}
