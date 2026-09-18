using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Proveedores;
using RRHH.WhatsApp.Infrastructure.Servicios;
using RRHH.WhatsApp.Tests.Proveedores;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// Circuito completo de ingesta: firma, normalizacion, persistencia idempotente, apertura de la
/// ventana de 24h, registro de opt-in y publicacion en la outbox.
/// <para>
/// Corre sobre el proveedor en memoria, asi que verifica la logica y no las restricciones de
/// SQL Server. El indice unico sobre ProviderMessageId que respalda la idempotencia bajo carrera
/// se comprueba al aplicar la migracion contra SQL Server.
/// </para>
/// </summary>
public class RecepcionWebhookTests : IDisposable
{
    private readonly RrhhDbContext _db;
    private readonly RecepcionWebhook _recepcion;
    private readonly ProveedorSimulado _proveedor;

    public RecepcionWebhookTests()
    {
        var opciones = new DbContextOptionsBuilder<RrhhDbContext>()
            .UseInMemoryDatabase($"recepcion-{Guid.NewGuid()}")
            // V28: la ingesta abre una transaccion por mensaje; lo atomico se prueba en E17 con SQL.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new RrhhDbContext(opciones);
        _db.Database.EnsureCreated();

        _proveedor = new ProveedorSimulado(TimeProvider.System, NullLogger<ProveedorSimulado>.Instance);

        _recepcion = new RecepcionWebhook(
            _proveedor,
            ServiciosDePrueba.Conversaciones(_db, TimeProvider.System),
            new MensajeService(_db, TimeProvider.System, NullLogger<MensajeService>.Instance),
            new EventoSistemaService(_db, TimeProvider.System, NullLogger<EventoSistemaService>.Instance),
            new UnidadTrabajoEf(_db),
            NullLogger<RecepcionWebhook>.Instance);
    }

    private static Dictionary<string, string> SinCabeceras() => [];

    [Fact]
    public async Task Crea_la_conversacion_y_guarda_el_mensaje_entrante()
    {
        var resultado = await _recepcion.ProcesarAsync(PayloadsDePrueba.MensajeDeTexto, SinCabeceras());

        Assert.True(resultado.FirmaValida);
        Assert.Equal(1, resultado.MensajesNuevos);

        var conversacion = Assert.Single(_db.Conversaciones);
        Assert.Equal("+51987654321", conversacion.TelefonoE164);

        var mensaje = Assert.Single(_db.Mensajes);
        Assert.Equal(DireccionMensaje.Entrante, mensaje.Direccion);
        Assert.Equal("Hola, vi el aviso de trabajo", mensaje.Contenido);
    }

    [Fact]
    public async Task La_conversacion_nace_sin_postulante_porque_todavia_no_hay_DNI()
    {
        // Cuando alguien escribe por primera vez solo tenemos su telefono; el DNI llega con el
        // JobForms. Es la desviacion V2 de docs/decisiones.md.
        await _recepcion.ProcesarAsync(PayloadsDePrueba.MensajeDeTexto, SinCabeceras());

        var conversacion = Assert.Single(_db.Conversaciones);

        Assert.Null(conversacion.PostulanteId);
        Assert.Null(conversacion.CuentaContextoId);
        // V30: nace en el menú del bot, no en «Sin clasificar»: todavía no hay nada que clasificar.
        Assert.Equal(EstadoConversacion.EnMenuBot, conversacion.Estado);
    }

    [Fact]
    public async Task Un_mensaje_entrante_registra_el_optin_y_abre_la_ventana_de_24h()
    {
        // Regla 15: que el postulante escriba primero es el consentimiento.
        await _recepcion.ProcesarAsync(PayloadsDePrueba.MensajeDeTexto, SinCabeceras());

        var conversacion = Assert.Single(_db.Conversaciones);

        Assert.NotNull(conversacion.FechaOptIn);
        Assert.Equal(OrigenOptIn.MensajeEntrante, conversacion.OrigenOptIn);
        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(1755600000).UtcDateTime,
            conversacion.FechaUltimoMensajeEntrante);
    }

    [Fact]
    public async Task El_optin_conserva_la_fecha_original_al_llegar_mas_mensajes()
    {
        // Lo que importa para auditoria es cuando se obtuvo el consentimiento, no el ultimo mensaje.
        await _recepcion.ProcesarAsync(PayloadsDePrueba.MensajeDeTexto, SinCabeceras());
        var primero = _db.Conversaciones.Single().FechaOptIn;

        await _recepcion.ProcesarAsync(PayloadsDePrueba.RespuestaDeBoton, SinCabeceras());
        var despues = _db.Conversaciones.Single();

        Assert.Equal(primero, despues.FechaOptIn);
        // La ventana de 24h si se mueve con cada entrante.
        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(1755600100).UtcDateTime,
            despues.FechaUltimoMensajeEntrante);
    }

    [Fact]
    public async Task Un_reintento_de_Meta_no_duplica_el_mensaje()
    {
        // Seccion 9.6.2: Meta reintrega si no recibe el 200 a tiempo.
        await _recepcion.ProcesarAsync(PayloadsDePrueba.MensajeDeTexto, SinCabeceras());
        var segundo = await _recepcion.ProcesarAsync(PayloadsDePrueba.MensajeDeTexto, SinCabeceras());

        Assert.Equal(0, segundo.MensajesNuevos);
        Assert.Equal(1, segundo.MensajesDuplicados);
        Assert.Single(_db.Mensajes);
    }

    [Fact]
    public async Task Un_reintento_tampoco_vuelve_a_encolar_el_evento()
    {
        // Encolarlo dos veces haria que el motor de reglas procese el mismo mensaje dos veces:
        // dos asignaciones, dos avisos, dos respuestas del bot.
        await _recepcion.ProcesarAsync(PayloadsDePrueba.MensajeDeTexto, SinCabeceras());
        await _recepcion.ProcesarAsync(PayloadsDePrueba.MensajeDeTexto, SinCabeceras());

        Assert.Single(_db.EventosSistema);
    }

    [Fact]
    public async Task Publica_el_evento_en_la_outbox_en_vez_de_procesar_las_reglas()
    {
        // El gateway es delgado: 360dialog da 5 segundos para responder 200.
        await _recepcion.ProcesarAsync(PayloadsDePrueba.MensajeDeTexto, SinCabeceras());

        var evento = Assert.Single(_db.EventosSistema);

        Assert.Equal(TiposEvento.MensajeEntranteRecibido, evento.Tipo);
        Assert.Equal(EstadoEvento.Pendiente, evento.Estado);
        Assert.NotEqual(Guid.Empty, evento.CorrelationId);
    }

    [Fact]
    public async Task El_mensaje_y_su_evento_comparten_el_CorrelationId()
    {
        // Es lo que permite trazar un mensaje de punta a punta cuando algo falla.
        await _recepcion.ProcesarAsync(PayloadsDePrueba.MensajeDeTexto, SinCabeceras());

        Assert.Equal(_db.Mensajes.Single().CorrelationId, _db.EventosSistema.Single().CorrelationId);
    }

    [Fact]
    public async Task Dos_mensajes_del_mismo_numero_comparten_una_sola_conversacion()
    {
        // WhatsApp entrega un unico hilo por numero de telefono (desviacion V1).
        var resultado = await _recepcion.ProcesarAsync(PayloadsDePrueba.DosMensajes, SinCabeceras());

        Assert.Equal(2, resultado.MensajesNuevos);
        Assert.Single(_db.Conversaciones);
        Assert.Equal(2, _db.Mensajes.Count());
    }

    [Fact]
    public async Task Rechaza_el_payload_cuando_la_firma_no_valida()
    {
        var proveedorConSecreto = new ProveedorSimulado(
            TimeProvider.System, NullLogger<ProveedorSimulado>.Instance, "secreto");

        var recepcion = new RecepcionWebhook(
            proveedorConSecreto,
            ServiciosDePrueba.Conversaciones(_db, TimeProvider.System),
            new MensajeService(_db, TimeProvider.System, NullLogger<MensajeService>.Instance),
            new EventoSistemaService(_db, TimeProvider.System, NullLogger<EventoSistemaService>.Instance),
            new UnidadTrabajoEf(_db),
            NullLogger<RecepcionWebhook>.Instance);

        var resultado = await recepcion.ProcesarAsync(
            PayloadsDePrueba.MensajeDeTexto,
            new Dictionary<string, string> { [Dialog360Provider.CabeceraFirma] = "firma-falsa" });

        Assert.False(resultado.FirmaValida);
        // Nada tocó la base: ni conversacion, ni mensaje, ni evento.
        Assert.Empty(_db.Conversaciones);
        Assert.Empty(_db.Mensajes);
        Assert.Empty(_db.EventosSistema);
    }

    [Fact]
    public async Task Un_acuse_de_entrega_actualiza_el_mensaje_saliente()
    {
        var conversacion = await ServiciosDePrueba.Conversaciones(_db, TimeProvider.System)
            .ObtenerOCrearAsync("+51987654321");

        var mensajes = new MensajeService(_db, TimeProvider.System, NullLogger<MensajeService>.Instance);

        await mensajes.RegistrarSalienteAsync(
            conversacion.ConversacionId, "Hola", null, null, "wamid.SALIENTE1", Guid.NewGuid());

        await _recepcion.ProcesarAsync(PayloadsDePrueba.AcusesDeEntrega, SinCabeceras());

        var saliente = _db.Mensajes.Single(m => m.ProviderMessageId == "wamid.SALIENTE1");
        Assert.Equal(EstadoEntrega.Entregado, saliente.EstadoEntrega);
    }

    [Fact]
    public async Task Un_acuse_fallido_guarda_el_error_del_proveedor()
    {
        var conversacion = await ServiciosDePrueba.Conversaciones(_db, TimeProvider.System)
            .ObtenerOCrearAsync("+51987654321");

        var mensajes = new MensajeService(_db, TimeProvider.System, NullLogger<MensajeService>.Instance);

        await mensajes.RegistrarSalienteAsync(
            conversacion.ConversacionId, "Hola", null, null, "wamid.SALIENTE2", Guid.NewGuid());

        await _recepcion.ProcesarAsync(PayloadsDePrueba.AcusesDeEntrega, SinCabeceras());

        var saliente = _db.Mensajes.Single(m => m.ProviderMessageId == "wamid.SALIENTE2");

        Assert.Equal(EstadoEntrega.Fallido, saliente.EstadoEntrega);
        Assert.Contains("131047", saliente.ErrorProveedor);
    }

    [Fact]
    public async Task Un_acuse_de_un_mensaje_desconocido_no_es_un_error()
    {
        // Puede venir de un mensaje enviado desde el panel de 360dialog: no hay nada que actualizar.
        var resultado = await _recepcion.ProcesarAsync(PayloadsDePrueba.AcusesDeEntrega, SinCabeceras());

        Assert.True(resultado.FirmaValida);
        Assert.Equal(2, resultado.EstadosActualizados);
        Assert.Empty(_db.Mensajes);
    }


    /// <summary>
    /// ARQ-13 (AL6): el payload es el minimo para volver a leer el mensaje. El telefono, el texto y el
    /// nombre de perfil quedan en las tablas, que si se purgan (Regla 17); la outbox no es un segundo
    /// almacen de datos personales.
    /// </summary>
    [Fact]
    public async Task El_payload_del_evento_no_lleva_datos_personales()
    {
        await _recepcion.ProcesarAsync(PayloadsDePrueba.MensajeDeTexto, SinCabeceras());

        var payload = _db.EventosSistema.Single().Payload;

        Assert.DoesNotContain("51987654321", payload);
        Assert.DoesNotContain("Maria Quispe", payload);
        Assert.DoesNotContain("Hola, vi el aviso de trabajo", payload);

        var campos = JsonDocument.Parse(payload).RootElement
            .EnumerateObject()
            .Select(p => p.Name.ToLowerInvariant())
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(["conversacionid", "fechaactividadanterior", "idbotonpulsado", "mensajeid"], campos);
    }

    [Fact]
    public async Task El_payload_identifica_el_mensaje_y_el_boton_pulsado()
    {
        await _recepcion.ProcesarAsync(PayloadsDePrueba.RespuestaDeBoton, SinCabeceras());

        var raiz = JsonDocument.Parse(_db.EventosSistema.Single().Payload).RootElement;

        Assert.Equal(_db.Conversaciones.Single().ConversacionId, raiz.GetProperty("conversacionId").GetInt32());
        Assert.Equal(_db.Mensajes.Single().MensajeId, raiz.GetProperty("mensajeId").GetInt64());
        Assert.Equal("cuenta_7", raiz.GetProperty("idBotonPulsado").GetString());
    }

    /// <summary>
    /// C1, A13: la instantanea se toma antes de registrar el entrante. Leerla despues daria siempre
    /// «hace un instante», porque el mismo mensaje ya movio FechaUltimaActividad, y la Regla 9 no
    /// volveria a preguntar la empresa nunca.
    /// </summary>
    [Fact]
    public async Task El_payload_lleva_la_actividad_anterior_al_mensaje()
    {
        await _recepcion.ProcesarAsync(PayloadsDePrueba.MensajeDeTexto, SinCabeceras());
        await _recepcion.ProcesarAsync(PayloadsDePrueba.RespuestaDeBoton, SinCabeceras());

        var segundo = _db.EventosSistema.OrderBy(e => e.EventoId).Last();
        var anterior = JsonDocument.Parse(segundo.Payload).RootElement
            .GetProperty("fechaActividadAnterior").GetDateTime();

        // La del primer entrante, no la del que acaba de llegar.
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1755600000).UtcDateTime, anterior);
    }

    public void Dispose() => _db.Dispose();
}
