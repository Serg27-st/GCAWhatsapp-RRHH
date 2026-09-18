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
    IAusenciaService ausencias,
    ICuentaService cuentas,
    TimeProvider reloj) : IFabricaContextoRegla
{
    public async Task<ContextoRegla> ParaMensajeEntranteAsync(
        int conversacionId, long mensajeId, string? idBotonPulsado, DateTime? fechaActividadAnterior,
        Guid correlationId, string claveEjecucion, CancellationToken ct = default)
    {
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var conversacion = await CargarConversacionAsync(conversacionId, ct);

        // El texto ya no viaja en el evento (ARQ-13): se relee de la tabla, que es donde se purga.
        var mensaje = await db.Mensajes.AsNoTracking().FirstOrDefaultAsync(m => m.MensajeId == mensajeId, ct);

        // La vacante se carga sin filtrar por estado: que este cerrada es informacion, no ausencia
        // de dato. Es lo que le permite a la Regla 20 decir que ya fue cubierta, en vez de tratar
        // el boton como si no existiera.
        var hc = await CargarVacanteAsync(IdsBoton.LeerVacante(idBotonPulsado), ct);

        // Si el postulante pulso un boton del menu, esa eleccion manda sobre el contexto anterior:
        // es como cambia de empresa sin abrir otra conversacion (Regla 6 sobre un hilo unico).
        // Un boton de vacante fija tambien la cuenta, porque la vacante ya la determina.
        var cuentaPorBoton = hc?.CuentaId ?? IdsBoton.LeerCuenta(idBotonPulsado);

        var origen = OrigenEleccion.Ninguna;
        int? cuentaId = null;

        if (cuentaPorBoton is not null)
        {
            cuentaId = cuentaPorBoton;
            origen = OrigenEleccion.Boton;
        }
        else if (idBotonPulsado is null)
        {
            // FUN-02 (A6): sin boton, lo que escribio puede identificar la vacante o la cuenta. Primero
            // el codigo del aviso, que es preciso; despues el nombre de la empresa, que es lo que el
            // postulante suele escribir. Recien si no reconoce nada entra el menu (R19).
            (hc, cuentaId, origen) = await ReconocerPorTextoAsync(hc, mensaje?.Contenido, ct);
        }

        // AL2: elegir no es lo mismo que heredar. La R19 solo cuenta un intento fallido si el postulante
        // no eligio nada, y la R9 no reenvia el menu de vacantes cuando el hilo ya venia con cuenta.
        if (cuentaId is null && conversacion.CuentaContextoId is { } delContexto)
        {
            cuentaId = delContexto;
            origen = OrigenEleccion.Contexto;
        }

        return await ArmarAsync(
            TipoDisparador.MensajeEntrante, conversacion, cuentaId, hc, postulacion: null, ahora, correlationId,
            claveEjecucion, ct, mensajeEntrante: mensaje, fechaActividadAnterior: fechaActividadAnterior,
            origenEleccion: origen,
            // FUN-03: pedir otra pagina del menu no es elegir; va aparte para que la R19 no lo cuente como fallo.
            paginaMenu: IdsBoton.LeerPagina(idBotonPulsado) ?? 0,
            postulacionElegidaId: IdsBoton.LeerProceso(idBotonPulsado),
            pidioOtraEmpresa: IdsBoton.EsOtraEmpresa(idBotonPulsado));
    }

    public async Task<ContextoRegla> ParaTiempoTranscurridoAsync(
        int conversacionId, string claveEjecucion, CancellationToken ct = default)
    {
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var conversacion = await CargarConversacionAsync(conversacionId, ct);

        return await ArmarAsync(
            TipoDisparador.TiempoTranscurrido, conversacion, conversacion.CuentaContextoId,
            hc: null, postulacion: null, ahora, Guid.NewGuid(), claveEjecucion, ct);
    }

    public async Task<ContextoRegla> ParaEnvioSalienteAsync(
        int conversacionId, Guid correlationId, string claveEjecucion, CancellationToken ct = default)
    {
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var conversacion = await CargarConversacionAsync(conversacionId, ct);

        return await ArmarAsync(
            TipoDisparador.EnvioSaliente, conversacion, conversacion.CuentaContextoId,
            hc: null, postulacion: null, ahora, correlationId, claveEjecucion, ct);
    }

    public async Task<ContextoRegla> ParaJobFormsCompletadoAsync(
        int conversacionId, int hcId, Guid correlationId, string claveEjecucion, CancellationToken ct = default)
    {
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var conversacion = await CargarConversacionAsync(conversacionId, ct);
        var hc = await CargarVacanteAsync(hcId, ct);

        return await ArmarAsync(
            TipoDisparador.JobFormsCompletado, conversacion,
            hc?.CuentaId ?? conversacion.CuentaContextoId, hc, postulacion: null, ahora, correlationId, claveEjecucion, ct);
    }


    public async Task<ContextoRegla> ParaTiempoPostulacionAsync(
        int postulacionId, string claveEjecucion, CancellationToken ct = default)
    {
        var ahora = reloj.GetUtcNow().UtcDateTime;

        var postulacion = await db.Postulaciones
            .AsNoTracking()
            .Include(p => p.Hc)
            .FirstAsync(p => p.PostulacionId == postulacionId, ct);

        // El hilo es por telefono y la postulacion por persona: al postulante se le responde por su
        // conversacion mas reciente, que es la que tiene la ventana de 24h mas fresca (Regla 15).
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
            TipoDisparador.TiempoTranscurridoPostulacion, conversacion, postulacion.CuentaId, postulacion.Hc, postulacion,
            ahora, Guid.NewGuid(), claveEjecucion, ct);
    }
    public async Task<ContextoRegla> ParaCambioEstadoPostulacionAsync(
        int postulacionId, Guid correlationId, string claveEjecucion, CancellationToken ct = default)
    {
        var ahora = reloj.GetUtcNow().UtcDateTime;

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
            postulacion.Hc, postulacion, ahora, correlationId, claveEjecucion, ct);

    }
    private async Task<ContextoRegla> ArmarAsync(
        TipoDisparador disparador,
        Conversacion conversacion,
        int? cuentaId,
        Hc? hc,
        Postulacion? postulacion,
        DateTime ahora,
        Guid correlationId,
        string claveEjecucion,
        CancellationToken ct,
        Mensaje? mensajeEntrante = null,
        DateTime? fechaActividadAnterior = null,
        OrigenEleccion origenEleccion = OrigenEleccion.Ninguna,
        int paginaMenu = 0,
        int? postulacionElegidaId = null,
        bool pidioOtraEmpresa = false)
    {
        var config = await configuracion.ObtenerTodasAsync(ct);

        var cuenta = cuentaId is { } id
            ? await db.Cuentas.AsNoTracking().FirstOrDefaultAsync(c => c.CuentaId == id && c.Activo, ct)
            : null;

        var (titular, respaldo) = await ResolverAnalistasAsync(cuenta?.CuentaId, ct);

        var titularAusente = titular is not null
            && await ausencias.EstaAusenteAsync(titular.AnalistaId, ahora, ct);

        var (reloj, habiles) = await CalcularEsperaAsync(conversacion, cuenta?.CuentaId, ahora, ct);

        // FUN-05 y FUN-06: los plazos de Jefatura corren en horas habiles. Un hilo que se escalo el
        // viernes a las 17:00 no esta vencido el sabado a las 19:00: nadie estuvo para atenderlo.
        var minutosEscalamiento = await MinutosHabilesDesdeAsync(conversacion.FechaEscalamiento, cuenta?.CuentaId, ahora, ct);
        var minutosPendiente = await MinutosHabilesDesdeAsync(conversacion.FechaPendienteDesde, cuenta?.CuentaId, ahora, ct);
        var minutosTextoNoReconocido = await MinutosHabilesDesdeAsync(conversacion.FechaTextoNoReconocido, cuenta?.CuentaId, ahora, ct);

        // La invitacion se resuelve antes del contexto porque de ella sale tambien el enlace: la
        // Regla 9 lo necesita como parametro de la plantilla y armar URLs no es tarea suya.
        var invitacion = await InvitacionPendienteAsync(conversacion.ConversacionId, ct);

        return new ContextoRegla
        {
            Disparador = disparador,
            AhoraUtc = ahora,
            CorrelationId = correlationId,
            ClaveEjecucion = claveEjecucion,
            Conversacion = conversacion,
            MensajeEntrante = mensajeEntrante,
            FechaActividadAnterior = fechaActividadAnterior,
            Postulante = conversacion.Postulante,
            Postulacion = postulacion,
            Cuenta = cuenta,
            Hc = hc,
            AnalistaTitular = titular,
            AnalistaRespaldo = respaldo,
            TitularAusente = titularAusente,
            DentroDeHorario = await horarios.EstaEnHorarioAsync(cuenta?.CuentaId, ahora, ct),
            VacantesAbiertas = await VacantesAbiertasAsync(cuenta?.CuentaId, ct),
            HcsConInvitacion = await HcsConInvitacionAsync(conversacion.ConversacionId, ct),
            Invitacion = invitacion,
            EnlaceInvitacion = EnlaceJobForms.Construir(invitacion?.Hc?.UrlJobForms, invitacion?.Token ?? Guid.Empty),
            PostulacionesDelPostulante = await PostulacionesDelPostulanteAsync(conversacion.PostulanteId, ct),
            InicioPeriodoFueraHorario = await horarios.InicioPeriodoFueraDeHorarioAsync(cuenta?.CuentaId, ahora, ct),
            ProximaApertura = await horarios.ProximaAperturaAsync(cuenta?.CuentaId, ahora, ct),
            DescripcionHorario = await horarios.DescribirHorarioAsync(cuenta?.CuentaId, ct),
            TransferenciaPendiente = await TransferenciaPendienteAsync(conversacion.ConversacionId, ct),
            MinutosHabilesDesdeEscalamiento = minutosEscalamiento,
            MinutosHabilesEnPendiente = minutosPendiente,
            MinutosHabilesDesdeTextoNoReconocido = minutosTextoNoReconocido,
            OrigenEleccion = origenEleccion,
            PaginaMenu = paginaMenu,
            PostulacionElegidaId = postulacionElegidaId,
            PidioOtraEmpresa = pidioOtraEmpresa,
            IntentosMenuFallidos = conversacion.IntentosMenuFallidos,
            MinutosSinRespuestaReloj = reloj,
            MinutosSinRespuestaHabiles = habiles,
            Configuracion = config
        };
    }


    /// <summary>
    /// R9, R16 y A3: en que anda el mismo DNI, en toda cuenta, con los nombres ya resueltos. Se traen
    /// todas y no solo las vivas porque la R16 archiva mirando las cerradas.
    /// </summary>
    private async Task<IReadOnlyList<PostulacionVigente>> PostulacionesDelPostulanteAsync(
        int? postulanteId, CancellationToken ct)
    {
        if (postulanteId is not { } id)
            return [];

        return await db.Postulaciones
            .AsNoTracking()
            .Where(p => p.PostulanteId == id)
            .OrderByDescending(p => p.FechaUltimaActividad)
            .Select(p => new PostulacionVigente(
                p.PostulacionId,
                p.CuentaId,
                p.Cuenta!.Nombre,
                p.HcId,
                p.Hc!.Titulo,
                p.Estado,
                p.AnalistaAsignadoId,
                p.FechaUltimaActividad))
            .ToListAsync(ct);
    }

    /// <summary>FUN-07 (A1): la transferencia que todavia espera respuesta. El indice unico garantiza una sola.</summary>
    private Task<Transferencia?> TransferenciaPendienteAsync(int conversacionId, CancellationToken ct) =>
        db.Transferencias
            .AsNoTracking()
            .Where(t => t.ConversacionId == conversacionId && t.Estado == EstadoTransferencia.Pendiente)
            .OrderByDescending(t => t.Fecha)
            .FirstOrDefaultAsync(ct);

    /// <summary>Minutos habiles desde un sello de seguimiento. Nulo si el sello no esta puesto (ARQ-08).</summary>
    private async Task<double?> MinutosHabilesDesdeAsync(
        DateTime? desde, int? cuentaId, DateTime ahora, CancellationToken ct) =>
        desde is { } momento ? await horarios.MinutosHabilesEntreAsync(cuentaId, momento, ahora, ct) : null;

    /// <summary>
    /// FUN-02: que eligio el postulante escribiendo. El codigo de aviso identifica una vacante —y con
    /// ella su cuenta—; el nombre de la empresa, solo la cuenta. La vacante se devuelve aunque este
    /// cerrada: es lo que le permite a la Regla 20 decir que ya fue cubierta (A6).
    /// </summary>
    private async Task<(Hc? Hc, int? CuentaId, OrigenEleccion Origen)> ReconocerPorTextoAsync(
        Hc? hc, string? contenido, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(contenido))
            return (hc, null, OrigenEleccion.Ninguna);

        var porCodigo = await cuentas.BuscarVacantePorCodigoAsync(
            [.. CodigoAviso.Candidatos(contenido)], ct);

        if (porCodigo is not null)
            return (porCodigo, porCodigo.CuentaId, OrigenEleccion.CodigoAviso);

        var porNombre = await cuentas.BuscarCuentaDeMenuPorNombreAsync(CodigoAviso.Normalizar(contenido), ct);

        return porNombre is not null
            ? (hc, porNombre.CuentaId, OrigenEleccion.NombreCuenta)
            : (hc, null, OrigenEleccion.Ninguna);
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

        // COR-09: sin formulario cargado no hay enlace que mandar, asi que la vacante no se ofrece
        // aunque este abierta; queda como alerta operativa para que alguien cargue la URL (V32).
        return await db.Hcs
            .AsNoTracking()
            .Where(h => h.CuentaId == id
                     && h.Estado == EstadoHc.Abierta
                     && h.UrlJobForms != null
                     && h.UrlJobForms != "")
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

    private Task<JobFormsInvitacion?> InvitacionPendienteAsync(int conversacionId, CancellationToken ct) =>
        db.JobFormsInvitaciones
            .AsNoTracking()
            .Include(i => i.Hc)
            .Where(i => i.ConversacionId == conversacionId && !i.Completado)
            .OrderByDescending(i => i.FechaEnvioLink)
            .FirstOrDefaultAsync(ct);

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
