using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Tests.Casos;

namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// E20 (FUN-16, AL6): el postulante pide que borren sus datos y no queda teléfono, texto ni nombre
/// en ninguna parte —mensajes, archivos, outbox y auditoría—, pero sí el historial que sostiene las
/// métricas de la Regla 18.
/// <para>
/// Antes la anonimización solo tocaba al postulante y su ficha: el teléfono seguía en la conversación,
/// el texto en los mensajes, el CV mandado por WhatsApp en el disco y el rastro en la outbox.
/// </para>
/// </summary>
public class E20AnonimizacionTests : IDisposable
{
    private const string Dni = "45678912";
    private const string Nombre = "Maria Quispe";

    private readonly ArnesEscenario _arnes = new();

    /// <summary>Un proceso completo: eligió empresa, completó el formulario, mandó su CV y le respondieron.</summary>
    private Task ConversacionCompletaAsync() => _arnes.ConversarAsync(
        new Entrante("Hola, busco trabajo"),
        new ConsumirOutbox(),
        new Boton("cuenta_7", "Alicorp"),
        new ConsumirOutbox(),
        new Despachar(),
        new Formulario(Dni, Nombre),
        new ConsumirOutbox(),
        new Despachar(),
        new Medio("document", "application/pdf", "CV Maria.pdf"),
        new DescargarAdjuntos(),
        new RespuestaAnalista("Te escribimos el lunes.", EntornoDeReglas.TitularId));

    private Task AnonimizarAsync() =>
        _arnes.Entorno.Postulantes.AnonimizarDatosAsync(Dni, "Solicitud del titular.");

    [Fact]
    public async Task Anonimizar_no_deja_telefono_ni_texto_ni_nombre()
    {
        await ConversacionCompletaAsync();

        var conversacion = await _arnes.ConversacionAsync();
        var adjunto = await _arnes.Entorno.Db.MensajesAdjuntos.AsNoTracking().SingleAsync();
        var archivo = Path.Combine(_arnes.Entorno.CarpetaAdjuntos, adjunto.Ruta!);

        Assert.True(File.Exists(archivo));

        await AnonimizarAsync();

        var postulante = await _arnes.Entorno.Db.Postulantes.AsNoTracking().SingleAsync();
        Assert.StartsWith("ANON-", postulante.Dni);
        Assert.Null(postulante.NombreCompleto);
        Assert.Null(postulante.TelefonoUltimo);

        // El teléfono es el dato personal de la conversación: sin él, el hilo ya no es de nadie.
        var anonimizada = await _arnes.Entorno.Db.Conversaciones.AsNoTracking()
            .SingleAsync(c => c.ConversacionId == conversacion.ConversacionId);

        Assert.Equal($"ANON-{conversacion.ConversacionId}", anonimizada.TelefonoE164);

        var mensajes = await _arnes.Entorno.Db.Mensajes.AsNoTracking()
            .Where(m => m.ConversacionId == conversacion.ConversacionId)
            .ToListAsync();

        Assert.NotEmpty(mensajes);
        Assert.All(mensajes, m =>
        {
            Assert.Equal("[anonimizado]", m.Contenido);
            Assert.Null(m.ParametrosPlantillaJson);
            Assert.Null(m.OpcionesJson);
        });

        // El archivo que mandó por WhatsApp es tan personal como el CV del formulario.
        var purgado = await _arnes.Entorno.Db.MensajesAdjuntos.AsNoTracking().SingleAsync();
        Assert.Equal(EstadoAdjunto.Purgado, purgado.Estado);
        Assert.Null(purgado.Ruta);
        Assert.False(File.Exists(archivo));

        // La outbox guarda identificadores (ARQ-13), pero hay histórico anterior a esa regla.
        var payloads = await _arnes.Entorno.Db.EventosSistema.AsNoTracking()
            .Select(e => e.Payload)
            .ToListAsync();

        Assert.All(payloads, p =>
        {
            Assert.DoesNotContain(conversacion.TelefonoE164, p);
            Assert.DoesNotContain(Nombre, p);
            Assert.DoesNotContain(Dni, p);
        });

        // La auditoría conserva qué pasó y cuándo; el detalle es donde se colaban los datos.
        var detalles = await _arnes.Entorno.Db.Auditorias.AsNoTracking()
            .Where(a => a.EntidadTipo == nameof(Conversacion) || a.EntidadTipo == nameof(Postulante))
            .Where(a => a.Accion != "AnonimizacionDatos")
            .Select(a => a.Detalle)
            .ToListAsync();

        Assert.All(detalles, Assert.Null);
    }

    /// <summary>Queda el rastro de que se anonimizó, con su motivo: es lo que se le muestra a quien audite.</summary>
    [Fact]
    public async Task Queda_la_constancia_de_la_anonimizacion()
    {
        await ConversacionCompletaAsync();
        await AnonimizarAsync();

        var registro = await _arnes.Entorno.Db.Auditorias.AsNoTracking()
            .SingleAsync(a => a.Accion == "AnonimizacionDatos");

        Assert.Equal("Solicitud del titular.", registro.Detalle);
    }

    /// <summary>Regla 18: las métricas se calculan sobre las postulaciones, que no se borran.</summary>
    [Fact]
    public async Task El_historial_que_sostiene_las_metricas_queda()
    {
        await ConversacionCompletaAsync();
        await AnonimizarAsync();

        var postulacion = await _arnes.Entorno.Db.Postulaciones.AsNoTracking().SingleAsync();

        Assert.Equal(EstadoPostulacion.EnProceso, postulacion.Estado);
        Assert.Equal(EntornoDeReglas.CuentaId, postulacion.CuentaId);
    }

    /// <summary>
    /// Si vuelve a escribir desde el mismo número, nace una conversación nueva con opt-in nuevo: es lo
    /// correcto después de pedir la eliminación, y el hilo viejo ya no se puede encontrar por teléfono.
    /// </summary>
    [Fact]
    public async Task Si_vuelve_a_escribir_nace_otra_conversacion()
    {
        await ConversacionCompletaAsync();

        var vieja = (await _arnes.ConversacionAsync()).ConversacionId;

        await AnonimizarAsync();

        await _arnes.ConversarAsync(new Entrante("Hola de nuevo"), new ConsumirOutbox(), new Despachar());

        var nueva = await _arnes.ConversacionAsync();

        Assert.NotEqual(vieja, nueva.ConversacionId);
        Assert.NotNull(nueva.FechaOptIn);
        Assert.Equal(2, await _arnes.Entorno.Db.Conversaciones.CountAsync());
    }

    public void Dispose() => _arnes.Dispose();
}
