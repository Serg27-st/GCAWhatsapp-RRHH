using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Servicios;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// T2.14 (ARQ-07): una prueba por accion nueva. El ejecutor es el unico que produce efectos, asi que
/// aca se comprueba que cada decision de una regla cambia lo que tiene que cambiar y nada mas.
/// </summary>
public class EjecutorAccionesNuevasTests : IDisposable
{
    private const int CuentaId = 7;
    private const int TitularId = 10;
    private const int JefaturaId = 12;

    private readonly RrhhDbContext _db;
    private readonly FakeTimeProvider _reloj = new(new DateTimeOffset(2026, 9, 14, 15, 0, 0, TimeSpan.Zero));
    private readonly EjecutorAcciones _ejecutor;
    private readonly ConversacionService _conversaciones;
    private readonly PostulacionService _postulaciones;
    private readonly PlantillaService _plantillas;

    public EjecutorAccionesNuevasTests()
    {
        _db = new RrhhDbContext(new DbContextOptionsBuilder<RrhhDbContext>()
            .UseInMemoryDatabase($"ejecutor-{Guid.NewGuid()}")
            .Options);

        _db.Database.EnsureCreated();
        Sembrar();

        _conversaciones = ServiciosDePrueba.Conversaciones(_db, _reloj);
        _postulaciones = new PostulacionService(_db, _reloj, NullLogger<PostulacionService>.Instance);
        _plantillas = new PlantillaService(_db, _reloj, NullLogger<PlantillaService>.Instance);

        _ejecutor = new EjecutorAcciones(
            _conversaciones,
            new MensajeService(_db, _reloj, NullLogger<MensajeService>.Instance),
            _plantillas,
            new JobFormsInvitacionService(_db, _reloj, NullLogger<JobFormsInvitacionService>.Instance),
            _postulaciones,
            new EventoSistemaService(_db, _reloj, NullLogger<EventoSistemaService>.Instance),
            new AuditoriaService(_db, _reloj),
            new AlertaOperativaService(_db, _reloj),
            new CuentaService(_db, new AlertaOperativaService(_db, _reloj), _reloj),
            ServiciosDePrueba.Analistas(_db, _reloj),
            NullLogger<EjecutorAcciones>.Instance);
    }

    private DateTime Ahora => _reloj.GetUtcNow().UtcDateTime;

    private void Sembrar()
    {
        _db.Cuentas.Add(new Cuenta { CuentaId = CuentaId, Nombre = "Alicorp", Activo = true });

        _db.Analistas.AddRange(
            new Analista { AnalistaId = TitularId, Nombre = "Ana Torres", Email = "ana@gca.pe", Activo = true },
            new Analista
            {
                AnalistaId = JefaturaId, Nombre = "Rosa Diaz", Email = "rosa@gca.pe",
                Rol = RolAnalista.Jefatura, Activo = true
            },
            new Analista
            {
                AnalistaId = 13, Nombre = "Jefe inactivo", Email = "ex@gca.pe",
                Rol = RolAnalista.Jefatura, Activo = false
            });

        _db.AnalistaCuentas.Add(new AnalistaCuenta
        {
            AnalistaCuentaId = 1, AnalistaId = TitularId, CuentaId = CuentaId, EsBackup = false
        });

        _db.Hcs.Add(new Hc
        {
            HcId = 1, CuentaId = CuentaId, Titulo = "Operario", Estado = EstadoHc.Abierta, FechaCreacion = Ahora
        });

        _db.Postulantes.Add(new Postulante
        {
            PostulanteId = 50, Dni = "45678912", NombreCompleto = "Maria Quispe", FechaRegistro = Ahora
        });

        _db.Postulaciones.Add(new Postulacion
        {
            PostulacionId = 1,
            PostulanteId = 50,
            HcId = 1,
            CuentaId = CuentaId,
            AnalistaAsignadoId = TitularId,
            EtapaKanbanId = 1,
            Estado = EstadoPostulacion.EnProceso,
            FechaCreacion = Ahora.AddDays(-30),
            FechaUltimaActividad = Ahora.AddDays(-30)
        });

        _db.SaveChanges();
    }

    /// <summary>El hilo tal como llega al ejecutor: con opt-in y con la ventana de 24h abierta (Regla 15).</summary>
    private async Task<Conversacion> ConversacionAsync(bool ventanaAbierta = true, int? postulanteId = null)
    {
        var conversacion = await _conversaciones.ObtenerOCrearAsync("+51987654321");

        conversacion.PostulanteId = postulanteId;
        conversacion.FechaOptIn = Ahora.AddDays(-30);
        conversacion.OrigenOptIn = OrigenOptIn.MensajeEntrante;
        conversacion.FechaUltimoMensajeEntrante = ventanaAbierta ? Ahora.AddHours(-1) : Ahora.AddDays(-3);

        await _db.SaveChangesAsync();

        return conversacion;
    }

    private async Task EjecutarAsync(Conversacion conversacion, params AccionRegla[] acciones)
    {
        var contexto = new ContextoRegla
        {
            Disparador = TipoDisparador.TiempoTranscurrido,
            AhoraUtc = Ahora,
            Conversacion = conversacion,
            Configuracion = new Dictionary<string, string>(),
            PostulacionesDelPostulante = await ProcesosAsync(conversacion.PostulanteId)
        };

        await _ejecutor.EjecutarAsync(acciones, contexto);
    }

    private async Task<IReadOnlyList<PostulacionVigente>> ProcesosAsync(int? postulanteId) =>
        postulanteId is null
            ? []
            : await _db.Postulaciones
                .Where(p => p.PostulanteId == postulanteId)
                .Select(p => new PostulacionVigente(
                    p.PostulacionId, p.CuentaId, p.Cuenta!.Nombre, p.HcId, p.Hc!.Titulo,
                    p.Estado, p.AnalistaAsignadoId, p.FechaUltimaActividad))
                .ToListAsync();

    private Task<Conversacion> RecargarAsync(int conversacionId) =>
        _db.Conversaciones.AsNoTracking().FirstAsync(c => c.ConversacionId == conversacionId);

    private Task<Postulacion> RecargarPostulacionAsync(int postulacionId) =>
        _db.Postulaciones.AsNoTracking().FirstAsync(p => p.PostulacionId == postulacionId);

    private Task<Mensaje?> UltimoSalienteAsync() =>
        _db.Mensajes.AsNoTracking()
            .Where(m => m.Direccion == DireccionMensaje.Saliente)
            .OrderByDescending(m => m.MensajeId)
            .FirstOrDefaultAsync();

    /// <summary>FUN-04 a FUN-06 (P4): cada aviso deja su sello; sin el, el barrido lo repite cada vuelta.</summary>
    [Theory]
    [InlineData(MarcaConversacion.AvisoFueraHorario)]
    [InlineData(MarcaConversacion.AvisoSegundoNivel)]
    [InlineData(MarcaConversacion.AvisoPendiente)]
    public async Task Sellar_la_conversacion_deja_la_fecha_del_aviso(MarcaConversacion marca)
    {
        var conversacion = await ConversacionAsync();

        await EjecutarAsync(conversacion, new SellarConversacion(marca));

        var recargada = await RecargarAsync(conversacion.ConversacionId);

        var sello = marca switch
        {
            MarcaConversacion.AvisoFueraHorario => recargada.FechaAvisoFueraHorario,
            MarcaConversacion.AvisoSegundoNivel => recargada.FechaAvisoSegundoNivel,
            _ => recargada.FechaAvisoPendiente
        };

        Assert.Equal(Ahora, sello);
    }

    /// <summary>COR-06 (AL2): el contador vive en la conversacion, no en el conteo de mensajes.</summary>
    [Fact]
    public async Task Registrar_un_intento_del_menu_suma_uno_y_sella_el_texto_no_reconocido()
    {
        var conversacion = await ConversacionAsync();

        await EjecutarAsync(conversacion, new RegistrarIntentoMenu(TextoNoReconocido: true));

        var recargada = await RecargarAsync(conversacion.ConversacionId);

        Assert.Equal(1, recargada.IntentosMenuFallidos);
        Assert.Equal(Ahora, recargada.FechaTextoNoReconocido);
    }

    /// <summary>A12: el plazo para derivar corre desde un texto que el bot no entendio, no desde el primer saludo.</summary>
    [Fact]
    public async Task El_primer_menu_suma_intento_pero_no_sella_texto_no_reconocido()
    {
        var conversacion = await ConversacionAsync();

        await EjecutarAsync(conversacion, new RegistrarIntentoMenu(TextoNoReconocido: false));

        var recargada = await RecargarAsync(conversacion.ConversacionId);

        Assert.Equal(1, recargada.IntentosMenuFallidos);
        Assert.Null(recargada.FechaTextoNoReconocido);
    }

    [Fact]
    public async Task Reiniciar_los_intentos_borra_el_contador_y_el_sello()
    {
        var conversacion = await ConversacionAsync();
        await EjecutarAsync(conversacion, new RegistrarIntentoMenu(TextoNoReconocido: true));

        await EjecutarAsync(conversacion, new ReiniciarIntentosMenu());

        var recargada = await RecargarAsync(conversacion.ConversacionId);

        Assert.Equal(0, recargada.IntentosMenuFallidos);
        Assert.Null(recargada.FechaTextoNoReconocido);
    }

    /// <summary>FUN-06 (P3): derivar deja plazo y rastro; sin FechaPendienteDesde nadie sabe desde cuando espera.</summary>
    [Fact]
    public async Task Derivar_a_pendientes_cambia_el_estado_sella_el_plazo_y_audita()
    {
        var conversacion = await ConversacionAsync();

        await EjecutarAsync(conversacion, new DerivarAPendientes("El bot no reconocio la respuesta."));

        var recargada = await RecargarAsync(conversacion.ConversacionId);

        Assert.Equal(EstadoConversacion.PendienteClasificar, recargada.Estado);
        Assert.Equal(Ahora, recargada.FechaPendienteDesde);
        Assert.Contains(_db.Auditorias, a => a.Accion == "DerivadaABandejaGeneral");
    }

    [Fact]
    public async Task Reactivar_saca_la_conversacion_de_archivada()
    {
        var conversacion = await ConversacionAsync();
        conversacion.Estado = EstadoConversacion.Archivada;
        await _db.SaveChangesAsync();

        await EjecutarAsync(conversacion, new ReactivarConversacion(EstadoConversacion.EnMenuBot));

        Assert.Equal(EstadoConversacion.EnMenuBot, (await RecargarAsync(conversacion.ConversacionId)).Estado);
    }

    /// <summary>FUN-08, FUN-09: quien ya esta en proceso vuelve con su analista, sin pasar por el menu.</summary>
    [Fact]
    public async Task Tomar_el_contexto_de_una_postulacion_fija_la_cuenta_y_su_analista()
    {
        var conversacion = await ConversacionAsync(postulanteId: 50);

        await EjecutarAsync(conversacion, new TomarContextoDePostulacion(1));

        var recargada = await RecargarAsync(conversacion.ConversacionId);

        Assert.Equal(CuentaId, recargada.CuentaContextoId);
        Assert.Equal(TitularId, recargada.AnalistaAtendiendoId);
        Assert.Equal(EstadoConversacion.Activa, recargada.Estado);
    }

    /// <summary>FUN-09: sin analista asignado en la postulacion, el hilo va al titular de la cuenta (Regla 1).</summary>
    [Fact]
    public async Task Sin_analista_asignado_el_contexto_lo_toma_el_titular_de_la_cuenta()
    {
        var postulacion = await _db.Postulaciones.FirstAsync(p => p.PostulacionId == 1);
        postulacion.AnalistaAsignadoId = null;
        await _db.SaveChangesAsync();

        var conversacion = await ConversacionAsync(postulanteId: 50);

        await EjecutarAsync(conversacion, new TomarContextoDePostulacion(1));

        Assert.Equal(TitularId, (await RecargarAsync(conversacion.ConversacionId)).AnalistaAtendiendoId);
    }

    [Fact]
    public async Task Archivar_la_postulacion_la_deja_en_estado_Archivada()
    {
        var conversacion = await ConversacionAsync(postulanteId: 50);

        await EjecutarAsync(conversacion, new ArchivarPostulacion(1, "90 dias sin actividad."));

        Assert.Equal(EstadoPostulacion.Archivada, (await RecargarPostulacionAsync(1)).Estado);
        Assert.Contains(_db.Auditorias, a => a.Accion == "Archivado" && a.EntidadTipo == nameof(Postulacion));
    }

    /// <summary>FUN-10 (A11): el cierre sale una sola vez; el sello es lo que lo garantiza (P4).</summary>
    [Fact]
    public async Task Sellar_el_cierre_de_cortesia_apaga_el_pendiente()
    {
        var postulacion = await _db.Postulaciones.FirstAsync(p => p.PostulacionId == 1);
        postulacion.CierreCortesiaPendiente = true;
        await _db.SaveChangesAsync();

        var conversacion = await ConversacionAsync(postulanteId: 50);

        await EjecutarAsync(conversacion, new SellarPostulacion(1, MarcaPostulacion.CierreCortesiaEnviado));

        var recargada = await RecargarPostulacionAsync(1);

        Assert.Equal(Ahora, recargada.FechaCierreCortesia);
        Assert.False(recargada.CierreCortesiaPendiente);
    }

    [Fact]
    public async Task Sellar_el_cierre_como_pendiente_lo_deja_para_el_barrido()
    {
        var conversacion = await ConversacionAsync(postulanteId: 50);

        await EjecutarAsync(conversacion, new SellarPostulacion(1, MarcaPostulacion.CierreCortesiaPendiente));

        var recargada = await RecargarPostulacionAsync(1);

        Assert.True(recargada.CierreCortesiaPendiente);
        Assert.Null(recargada.FechaCierreCortesia);
    }

    [Fact]
    public async Task Sellar_el_aviso_de_archivado_deja_su_fecha()
    {
        var conversacion = await ConversacionAsync(postulanteId: 50);

        await EjecutarAsync(conversacion, new SellarPostulacion(1, MarcaPostulacion.AvisoArchivado));

        Assert.Equal(Ahora, (await RecargarPostulacionAsync(1)).FechaAvisoArchivado);
    }

    /// <summary>FUN-05: el aviso es para el rol, porque quien esta en Jefatura hoy cambia sin que cambie la regla.</summary>
    [Fact]
    public async Task Notificar_a_un_rol_avisa_a_cada_analista_activo_de_ese_rol()
    {
        var conversacion = await ConversacionAsync();

        await EjecutarAsync(conversacion, new NotificarRol(RolAnalista.Jefatura, "Hay un hilo sin respuesta."));

        var avisos = _db.EventosSistema.Where(e => e.Tipo == TiposEvento.AnalistaNotificado).ToList();

        var aviso = Assert.Single(avisos);
        Assert.Contains($"\"analistaId\":{JefaturaId}", aviso.Payload);
    }

    /// <summary>COR-03 (P1, C3): con la ventana abierta el bot habla en texto, sin esperar a Meta.</summary>
    [Fact]
    public async Task El_mensaje_del_bot_sale_como_texto_dentro_de_la_ventana()
    {
        var conversacion = await ConversacionAsync();

        await EjecutarAsync(conversacion, new EnviarMensajeBot(
            "Gracias Maria. Recibimos tu ficha.", ClavesPlantilla.ConfirmacionJobForms, ["Maria"]));

        var saliente = await UltimoSalienteAsync();

        Assert.Equal(TipoSaliente.Texto, saliente?.TipoSaliente);
        Assert.Equal("Gracias Maria. Recibimos tu ficha.", saliente?.Contenido);
        Assert.Equal(EstadoEntrega.EnCola, saliente?.EstadoEntrega);
    }

    /// <summary>Regla 15: cerrada la ventana solo puede salir plantilla aprobada.</summary>
    [Fact]
    public async Task Fuera_de_la_ventana_el_mensaje_del_bot_sale_como_plantilla_activa()
    {
        var conversacion = await ConversacionAsync(ventanaAbierta: false);
        await ActivarPlantillaAsync(ClavesPlantilla.ConfirmacionJobForms);

        await EjecutarAsync(conversacion, new EnviarMensajeBot(
            "Gracias Maria. Recibimos tu ficha.", ClavesPlantilla.ConfirmacionJobForms, ["Maria", "Operario"]));

        var saliente = await UltimoSalienteAsync();

        Assert.Equal(TipoSaliente.Plantilla, saliente?.TipoSaliente);
        Assert.NotNull(saliente?.PlantillaId);
    }

    /// <summary>
    /// V32, COR-03: sin ventana y sin plantilla aprobada no hay envio posible. Queda la alerta para que
    /// alguien la apruebe, agrupada por plantilla y no una por postulante (M1).
    /// </summary>
    [Fact]
    public async Task Sin_ventana_y_sin_plantilla_activa_no_sale_nada_y_queda_una_alerta()
    {
        var conversacion = await ConversacionAsync(ventanaAbierta: false);

        await EjecutarAsync(conversacion, new EnviarMensajeBot(
            "Gracias Maria.", ClavesPlantilla.ConfirmacionJobForms, ["Maria"]));

        Assert.Null(await UltimoSalienteAsync());

        var alerta = Assert.Single(_db.AlertasOperativas);
        Assert.Equal(TiposAlerta.PlantillaNoAprobada, alerta.Tipo);
    }

    [Fact]
    public async Task Un_mensaje_del_bot_sin_plantilla_y_con_la_ventana_cerrada_tampoco_sale()
    {
        var conversacion = await ConversacionAsync(ventanaAbierta: false);

        await EjecutarAsync(conversacion, new EnviarMensajeBot("Un aviso cualquiera.", null, []));

        Assert.Null(await UltimoSalienteAsync());
    }

    /// <summary>
    /// COR-03: el sello va atado al envio. Sellar un recordatorio que no salio lo pierde para siempre:
    /// el barrido lo da por hecho y el postulante nunca lo recibe.
    /// </summary>
    [Fact]
    public async Task Lo_que_solo_se_sella_si_se_envio_no_se_sella_cuando_el_envio_no_salio()
    {
        var conversacion = await ConversacionAsync(ventanaAbierta: false, postulanteId: 50);

        await EjecutarAsync(
            conversacion,
            new EnviarMensajeBot("Cierre de cortesia.", ClavesPlantilla.CierreCortesia, ["Maria", "Operario"]),
            new SellarPostulacion(1, MarcaPostulacion.CierreCortesiaEnviado) { SoloSiSeEnvioAnterior = true });

        Assert.Null((await RecargarPostulacionAsync(1)).FechaCierreCortesia);
    }

    [Fact]
    public async Task Lo_que_solo_se_sella_si_se_envio_si_se_sella_cuando_el_envio_salio()
    {
        var conversacion = await ConversacionAsync(postulanteId: 50);
        await ActivarPlantillaAsync(ClavesPlantilla.CierreCortesia);

        await EjecutarAsync(
            conversacion,
            new EnviarMensajeBot("Cierre de cortesia.", ClavesPlantilla.CierreCortesia, ["Maria", "Operario"]),
            new SellarPostulacion(1, MarcaPostulacion.CierreCortesiaEnviado) { SoloSiSeEnvioAnterior = true });

        Assert.Equal(Ahora, (await RecargarPostulacionAsync(1)).FechaCierreCortesia);
    }

    /// <summary>FUN-09 (A3): el postulante elige entre sus procesos vivos, con nombre y con salida.</summary>
    [Fact]
    public async Task El_menu_de_procesos_ofrece_los_procesos_vivos_y_la_salida_a_otra_empresa()
    {
        var conversacion = await ConversacionAsync(postulanteId: 50);

        await EjecutarAsync(conversacion, new MostrarMenuProcesos());

        var saliente = await UltimoSalienteAsync();

        Assert.NotNull(saliente?.OpcionesJson);
        Assert.Contains(IdsBoton.ParaProceso(1), saliente!.OpcionesJson);
        Assert.Contains("Alicorp", saliente.OpcionesJson);
        Assert.Contains(IdsBoton.OtraEmpresa, saliente.OpcionesJson);
    }

    /// <summary>Un proceso ya cerrado no se ofrece: elegirlo no llevaria a ninguna parte.</summary>
    [Fact]
    public async Task El_menu_de_procesos_deja_fuera_los_procesos_cerrados()
    {
        var postulacion = await _db.Postulaciones.FirstAsync(p => p.PostulacionId == 1);
        postulacion.Estado = EstadoPostulacion.Descartado;
        await _db.SaveChangesAsync();

        var conversacion = await ConversacionAsync(postulanteId: 50);

        await EjecutarAsync(conversacion, new MostrarMenuProcesos());

        var saliente = await UltimoSalienteAsync();

        Assert.DoesNotContain(IdsBoton.ParaProceso(1), saliente?.OpcionesJson);
    }

    /// <summary>
    /// Las 6 plantillas sembradas nacen inactivas a proposito, hasta que Meta las apruebe. Una prueba
    /// que necesita el camino con plantilla activa la activa aca, nunca el codigo de produccion.
    /// </summary>
    private async Task ActivarPlantillaAsync(params string[] claves)
    {
        foreach (var plantilla in await _db.Plantillas.Where(p => claves.Contains(p.Clave)).ToListAsync())
            plantilla.Activa = true;

        await _db.SaveChangesAsync();
    }

    public void Dispose() => _db.Dispose();
}
