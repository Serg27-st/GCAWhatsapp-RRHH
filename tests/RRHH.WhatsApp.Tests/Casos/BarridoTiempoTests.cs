using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Tests.Proveedores;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// Regla 2 de punta a punta. Es la unica regla que no dispara un mensaje entrante sino el paso del
/// tiempo, asi que sin el barrido del Worker quedaria escrita y sin correr nunca.
/// </summary>
public class BarridoTiempoTests : IDisposable
{
    private readonly EntornoDeReglas _entorno = new();

    private static Dictionary<string, string> SinCabeceras() => [];

    /// <summary>Deja una conversacion asignada al titular y con un mensaje sin responder.</summary>
    private async Task<int> ConversacionAsignadaAsync()
    {
        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);
        await _entorno.ConsumirOutboxAsync();

        var conversacion = await _entorno.Db.Conversaciones.FirstAsync();

        Assert.Equal(EntornoDeReglas.TitularId, conversacion.AnalistaAtendiendoId);

        return conversacion.ConversacionId;
    }

    private async Task AtrasarActividadAsync(int conversacionId, TimeSpan atraso)
    {
        var conversacion = await _entorno.Db.Conversaciones.FirstAsync(c => c.ConversacionId == conversacionId);

        var momento = _entorno.Ahora - atraso;

        conversacion.FechaUltimoMensajeEntrante = momento;
        conversacion.FechaUltimaActividad = momento;

        await _entorno.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Sin_horario_cargado_el_titular_que_no_responde_en_2_horas_pierde_la_conversacion()
    {
        var id = await ConversacionAsignadaAsync();

        await AtrasarActividadAsync(id, TimeSpan.FromHours(3));

        var acciones = await _entorno.Barrido.ProcesarConversacionAsync(id);

        Assert.True(acciones > 0);

        var conversacion = await _entorno.Db.Conversaciones.FirstAsync(c => c.ConversacionId == id);

        Assert.Equal(EntornoDeReglas.RespaldoId, conversacion.AnalistaAtendiendoId);
        Assert.Equal(EstadoConversacion.Escalada, conversacion.Estado);
    }

    [Fact]
    public async Task Antes_de_las_2_horas_no_escala()
    {
        var id = await ConversacionAsignadaAsync();

        await AtrasarActividadAsync(id, TimeSpan.FromMinutes(30));

        var acciones = await _entorno.Barrido.ProcesarConversacionAsync(id);

        Assert.Equal(0, acciones);

        var conversacion = await _entorno.Db.Conversaciones.FirstAsync(c => c.ConversacionId == id);

        Assert.Equal(EntornoDeReglas.TitularId, conversacion.AnalistaAtendiendoId);
    }

    [Fact]
    public async Task Si_el_analista_ya_respondio_no_hay_nada_que_escalar()
    {
        var id = await ConversacionAsignadaAsync();

        await AtrasarActividadAsync(id, TimeSpan.FromHours(3));

        // Es lo que detiene el reloj de la Regla 2. Sin esta marca, el barrido escalaria
        // conversaciones que ya fueron atendidas.
        await _entorno.Conversaciones.RegistrarRespuestaAnalistaAsync(id, _entorno.Ahora);

        var acciones = await _entorno.Barrido.ProcesarConversacionAsync(id);

        Assert.Equal(0, acciones);

        var conversacion = await _entorno.Db.Conversaciones.FirstAsync(c => c.ConversacionId == id);

        Assert.Equal(EntornoDeReglas.TitularId, conversacion.AnalistaAtendiendoId);
    }

    [Fact]
    public async Task La_consulta_de_candidatas_trae_el_hilo_sin_responder_y_descarta_el_atendido()
    {
        var id = await ConversacionAsignadaAsync();

        await AtrasarActividadAsync(id, TimeSpan.FromHours(3));

        var pendientes = await _entorno.Conversaciones.ListarPendientesEscalamientoAsync(50);
        Assert.Contains(id, pendientes);

        await _entorno.Conversaciones.RegistrarRespuestaAnalistaAsync(id, _entorno.Ahora);

        var trasResponder = await _entorno.Conversaciones.ListarPendientesEscalamientoAsync(50);
        Assert.DoesNotContain(id, trasResponder);
    }

    [Fact]
    public async Task Una_conversacion_ya_escalada_no_vuelve_a_escalar()
    {
        // El respaldo es el ultimo eslabon: reescalar la devolveria a si mismo en cada barrido.
        var id = await ConversacionAsignadaAsync();

        await AtrasarActividadAsync(id, TimeSpan.FromHours(3));
        await _entorno.Barrido.ProcesarConversacionAsync(id);

        await AtrasarActividadAsync(id, TimeSpan.FromHours(6));

        var acciones = await _entorno.Barrido.ProcesarConversacionAsync(id);

        Assert.Equal(0, acciones);
    }

    public void Dispose() => _entorno.Dispose();
}
