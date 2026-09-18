using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Tests.Proveedores;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// COR-13 (M4): el barrido lee el estado en un ámbito y ejecuta en otro. Entre las dos cosas el
/// analista pudo haber respondido, o el hilo pudo haber cambiado de manos, y escalarlo entonces se
/// lo quita a quien lo tiene.
/// </summary>
public class EscalamientoSinCarreraTests : IDisposable
{
    private readonly EntornoDeReglas _entorno = new();

    /// <summary>Un hilo asignado al titular, con un entrante sin responder de hace tres horas.</summary>
    private async Task<int> ConEsperaDeTresHorasAsync()
    {
        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);
        await _entorno.ConsumirOutboxAsync();

        var conversacion = await _entorno.Db.Conversaciones.FirstAsync();

        conversacion.FechaUltimoMensajeEntrante = _entorno.Ahora.AddHours(-3);
        conversacion.FechaUltimaRespuestaAnalista = null;

        await _entorno.Db.SaveChangesAsync();

        return conversacion.ConversacionId;
    }

    private Task<Domain.Entidades.Conversacion> LeerAsync(int id) =>
        _entorno.Db.Conversaciones.AsNoTracking().FirstAsync(c => c.ConversacionId == id);

    [Fact]
    public async Task Sin_respuesta_del_titular_el_barrido_escala_al_respaldo()
    {
        var id = await ConEsperaDeTresHorasAsync();

        await _entorno.Barrido.ProcesarConversacionAsync(id);

        var conversacion = await LeerAsync(id);

        Assert.Equal(EntornoDeReglas.RespaldoId, conversacion.AnalistaAtendiendoId);
        Assert.Equal(EstadoConversacion.Escalada, conversacion.Estado);

        // FUN-05 (A9): desde este sello corre el plazo del segundo nivel.
        Assert.Equal(_entorno.Ahora, conversacion.FechaEscalamiento);
    }

    /// <summary>El caso de M4: el titular contesta justo mientras el barrido esta escalando.</summary>
    [Fact]
    public async Task Si_el_titular_respondio_mientras_tanto_no_se_escala()
    {
        var id = await ConEsperaDeTresHorasAsync();

        await _entorno.Conversaciones.EscalarAsync(
            id, EntornoDeReglas.TitularId, EntornoDeReglas.RespaldoId, "El titular no respondio.");

        Assert.Equal(EstadoConversacion.Escalada, (await LeerAsync(id)).Estado);

        // Otra vuelta del barrido sobre una conversacion ya respondida: no vuelve a escalar.
        await _entorno.Conversaciones.RegistrarRespuestaAnalistaAsync(id, _entorno.Ahora);

        await _entorno.Conversaciones.EscalarAsync(
            id, EntornoDeReglas.RespaldoId, EntornoDeReglas.TitularId, "Segundo intento.");

        Assert.Equal(EntornoDeReglas.RespaldoId, (await LeerAsync(id)).AnalistaAtendiendoId);
    }

    [Fact]
    public async Task Si_la_conversacion_cambio_de_manos_no_se_escala()
    {
        var id = await ConEsperaDeTresHorasAsync();

        // Una transferencia aceptada la dejo con otro analista despues de armado el contexto.
        await _entorno.Conversaciones.AsignarAnalistaAsync(id, EntornoDeReglas.RespaldoId, "Transferida.");

        await _entorno.Conversaciones.EscalarAsync(
            id, EntornoDeReglas.TitularId, EntornoDeReglas.RespaldoId, "El titular no respondio.");

        var conversacion = await LeerAsync(id);

        Assert.Equal(EntornoDeReglas.RespaldoId, conversacion.AnalistaAtendiendoId);
        Assert.NotEqual(EstadoConversacion.Escalada, conversacion.Estado);
        Assert.Null(conversacion.FechaEscalamiento);
    }

    public void Dispose() => _entorno.Dispose();
}
