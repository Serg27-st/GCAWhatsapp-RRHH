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

    private Task<int> AvisosParaAsync(int analistaId) =>
        _entorno.Db.EventosSistema.CountAsync(
            e => e.Tipo == TiposEvento.AnalistaNotificado
              && e.Payload.Contains($"\"AnalistaId\":{analistaId}"));

    private Task<Domain.Entidades.Transferencia> TransferirNormalAsync(int conversacionId, string? comentario = null) =>
        _entorno.Conversaciones.TransferirAsync(
            conversacionId, EntornoDeReglas.TitularId, EntornoDeReglas.RespaldoId, urgente: false, comentario);

    [Fact]
    public async Task El_destino_ve_la_transferencia_con_quien_la_envia()
    {
        var id = await ConversacionAsignadaAsync();

        await TransferirNormalAsync(id, "Encaja mejor en tu vacante.");

        var pendiente = Assert.Single(
            await _entorno.Conversaciones.ListarTransferenciasPendientesAsync(EntornoDeReglas.RespaldoId));

        Assert.Equal(id, pendiente.ConversacionId);
        Assert.Equal("Ana Torres", pendiente.AnalistaOrigen?.Nombre);
        Assert.Equal("Encaja mejor en tu vacante.", pendiente.Comentario);
    }

    [Fact]
    public async Task Quien_la_envia_no_la_ve_como_pendiente_suya()
    {
        var id = await ConversacionAsignadaAsync();

        await TransferirNormalAsync(id);

        Assert.Empty(await _entorno.Conversaciones.ListarTransferenciasPendientesAsync(EntornoDeReglas.TitularId));
    }

    [Fact]
    public async Task Una_urgente_no_queda_esperando_respuesta()
    {
        var id = await ConversacionAsignadaAsync();

        await _entorno.Conversaciones.TransferirAsync(
            id, EntornoDeReglas.TitularId, EntornoDeReglas.RespaldoId, urgente: true, null);

        Assert.Empty(await _entorno.Conversaciones.ListarTransferenciasPendientesAsync(EntornoDeReglas.RespaldoId));
    }

    [Fact]
    public async Task Responderla_la_saca_de_la_lista()
    {
        var id = await ConversacionAsignadaAsync();
        var transferencia = await TransferirNormalAsync(id);

        await _entorno.Conversaciones.ResponderTransferenciaAsync(
            transferencia.TransferenciaId, EntornoDeReglas.RespaldoId, aceptada: true);

        Assert.Empty(await _entorno.Conversaciones.ListarTransferenciasPendientesAsync(EntornoDeReglas.RespaldoId));
    }

    /// <summary>
    /// Rechazar deja el hilo con el origen, y es el origen quien tiene que derivarlo a otro. Sin
    /// este aviso no sabria que le toca.
    /// </summary>
    [Fact]
    public async Task Rechazarla_avisa_al_origen()
    {
        var id = await ConversacionAsignadaAsync();
        var transferencia = await TransferirNormalAsync(id);
        var antes = await AvisosParaAsync(EntornoDeReglas.TitularId);

        await _entorno.Conversaciones.ResponderTransferenciaAsync(
            transferencia.TransferenciaId, EntornoDeReglas.RespaldoId, aceptada: false);

        Assert.Equal(antes + 1, await AvisosParaAsync(EntornoDeReglas.TitularId));
    }

    [Fact]
    public async Task Aceptarla_tambien_avisa_al_origen()
    {
        var id = await ConversacionAsignadaAsync();
        var transferencia = await TransferirNormalAsync(id);
        var antes = await AvisosParaAsync(EntornoDeReglas.TitularId);

        await _entorno.Conversaciones.ResponderTransferenciaAsync(
            transferencia.TransferenciaId, EntornoDeReglas.RespaldoId, aceptada: true);

        Assert.Equal(antes + 1, await AvisosParaAsync(EntornoDeReglas.TitularId));
    }

    [Fact]
    public async Task Solo_el_destino_puede_responderla()
    {
        var id = await ConversacionAsignadaAsync();
        var transferencia = await TransferirNormalAsync(id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _entorno.Conversaciones.ResponderTransferenciaAsync(
                transferencia.TransferenciaId, EntornoDeReglas.TitularId, aceptada: true));
    }

    [Fact]
    public async Task No_se_responde_dos_veces()
    {
        var id = await ConversacionAsignadaAsync();
        var transferencia = await TransferirNormalAsync(id);

        await _entorno.Conversaciones.ResponderTransferenciaAsync(
            transferencia.TransferenciaId, EntornoDeReglas.RespaldoId, aceptada: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _entorno.Conversaciones.ResponderTransferenciaAsync(
                transferencia.TransferenciaId, EntornoDeReglas.RespaldoId, aceptada: false));
    }

    /// <summary>V23: Jefatura y Sistemas no atienden, asi que tampoco reciben conversaciones.</summary>
    [Fact]
    public async Task No_se_transfiere_a_quien_no_atiende_conversaciones()
    {
        var id = await ConversacionAsignadaAsync();

        _entorno.Db.Analistas.Add(new Domain.Entidades.Analista
        {
            AnalistaId = 20, Nombre = "Jefa del area", Email = "jefa@gca.pe",
            Activo = true, Rol = RolAnalista.Jefatura
        });

        await _entorno.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _entorno.Conversaciones.TransferirAsync(id, EntornoDeReglas.TitularId, 20, urgente: true, null));
    }

    public void Dispose() => _entorno.Dispose();
}
