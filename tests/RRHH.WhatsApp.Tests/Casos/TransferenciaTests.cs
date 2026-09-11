using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Tests.Proveedores;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// Regla 8 — transferencia de conversación, de un analista a la vez.
/// <para>
/// El aviso al destino es parte de la regla, no un adorno: una transferencia que espera aceptación
/// y que nadie sabe que llegó se queda esperando para siempre.
/// </para>
/// </summary>
public class TransferenciaTests : IDisposable
{
    private readonly EntornoDeReglas _entorno = new();

    private async Task<int> ConversacionAsignadaAsync()
    {
        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);
        await _entorno.ConsumirOutboxAsync();

        return (await _entorno.Db.Conversaciones.FirstAsync()).ConversacionId;
    }

    private Task<bool> HayAvisoParaAsync(int analistaId) =>
        _entorno.Db.EventosSistema.AnyAsync(
            e => e.Tipo == TiposEvento.AnalistaNotificado
              && e.Payload.Contains($"\"AnalistaId\":{analistaId}"));

    [Fact]
    public async Task Una_transferencia_normal_queda_pendiente_de_aceptacion()
    {
        var id = await ConversacionAsignadaAsync();

        var transferencia = await _entorno.Conversaciones.TransferirAsync(
            id, EntornoDeReglas.TitularId, EntornoDeReglas.RespaldoId,
            urgente: false, "Encaja mejor en tu vacante.");

        Assert.Equal(EstadoTransferencia.Pendiente, transferencia.Estado);

        // El hilo sigue con el titular hasta que el destino acepte.
        var conversacion = await _entorno.Db.Conversaciones.AsNoTracking()
            .FirstAsync(c => c.ConversacionId == id);

        Assert.Equal(EntornoDeReglas.TitularId, conversacion.AnalistaAtendiendoId);
    }

    [Fact]
    public async Task El_analista_destino_recibe_un_aviso()
    {
        var id = await ConversacionAsignadaAsync();

        await _entorno.Conversaciones.TransferirAsync(
            id, EntornoDeReglas.TitularId, EntornoDeReglas.RespaldoId, urgente: false, null);

        Assert.True(await HayAvisoParaAsync(EntornoDeReglas.RespaldoId));
    }

    [Fact]
    public async Task Una_transferencia_urgente_se_aplica_sola_y_tambien_avisa()
    {
        var id = await ConversacionAsignadaAsync();

        var transferencia = await _entorno.Conversaciones.TransferirAsync(
            id, EntornoDeReglas.TitularId, EntornoDeReglas.RespaldoId, urgente: true, null);

        Assert.Equal(EstadoTransferencia.Aceptada, transferencia.Estado);
        Assert.NotNull(transferencia.FechaRespuesta);

        var conversacion = await _entorno.Db.Conversaciones.AsNoTracking()
            .FirstAsync(c => c.ConversacionId == id);

        Assert.Equal(EntornoDeReglas.RespaldoId, conversacion.AnalistaAtendiendoId);
        Assert.True(await HayAvisoParaAsync(EntornoDeReglas.RespaldoId));
    }

    [Fact]
    public async Task Aceptar_la_transferencia_pasa_el_hilo_al_destino()
    {
        var id = await ConversacionAsignadaAsync();

        var transferencia = await _entorno.Conversaciones.TransferirAsync(
            id, EntornoDeReglas.TitularId, EntornoDeReglas.RespaldoId, urgente: false, null);

        await _entorno.Conversaciones.ResponderTransferenciaAsync(
            transferencia.TransferenciaId, EntornoDeReglas.RespaldoId, aceptada: true);

        var conversacion = await _entorno.Db.Conversaciones.AsNoTracking()
            .FirstAsync(c => c.ConversacionId == id);

        Assert.Equal(EntornoDeReglas.RespaldoId, conversacion.AnalistaAtendiendoId);
    }

    [Fact]
    public async Task Rechazarla_deja_el_hilo_donde_estaba()
    {
        var id = await ConversacionAsignadaAsync();

        var transferencia = await _entorno.Conversaciones.TransferirAsync(
            id, EntornoDeReglas.TitularId, EntornoDeReglas.RespaldoId, urgente: false, null);

        await _entorno.Conversaciones.ResponderTransferenciaAsync(
            transferencia.TransferenciaId, EntornoDeReglas.RespaldoId, aceptada: false);

        var conversacion = await _entorno.Db.Conversaciones.AsNoTracking()
            .FirstAsync(c => c.ConversacionId == id);

        Assert.Equal(EntornoDeReglas.TitularId, conversacion.AnalistaAtendiendoId);
    }

    public void Dispose() => _entorno.Dispose();
}
