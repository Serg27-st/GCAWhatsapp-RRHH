using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Tests.Proveedores;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// FUN-07: retirar es la salida del lado de quien envió la transferencia. Sin ella, una derivación
/// mal dirigida solo se deshacía esperando el vencimiento, y mientras tanto bloqueaba cualquier otra
/// transferencia de esa conversación (V21).
/// </summary>
public class RetiroTransferenciaTests : IDisposable
{
    private const int TerceroId = 12;

    private readonly EntornoDeReglas _entorno = new();

    private async Task<Transferencia> ConTransferenciaAsync()
    {
        _entorno.Db.Analistas.Add(new Analista
        {
            AnalistaId = TerceroId, Nombre = "Rosa Diaz", Email = "rosa@gca.pe", Activo = true
        });

        await _entorno.Db.SaveChangesAsync();

        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);
        await _entorno.ConsumirOutboxAsync();

        var conversacion = await _entorno.Db.Conversaciones.FirstAsync();

        return await _entorno.Conversaciones.TransferirAsync(
            conversacion.ConversacionId, EntornoDeReglas.TitularId, TerceroId, urgente: false, "¿La tomás?");
    }

    private Task<Transferencia> LeerAsync(int id) =>
        _entorno.Db.Transferencias.AsNoTracking().FirstAsync(t => t.TransferenciaId == id);

    [Fact]
    public async Task Quien_la_envio_puede_retirarla_mientras_nadie_responda()
    {
        var transferencia = await ConTransferenciaAsync();

        await _entorno.Conversaciones.RetirarTransferenciaAsync(
            transferencia.TransferenciaId, EntornoDeReglas.TitularId);

        var retirada = await LeerAsync(transferencia.TransferenciaId);

        Assert.Equal(EstadoTransferencia.Retirada, retirada.Estado);
        Assert.NotNull(retirada.FechaRespuesta);
        Assert.Contains(_entorno.Db.Auditorias, a => a.Accion == "TransferenciaRetirada");
    }

    /// <summary>El destino la tenía esperando respuesta: tiene que enterarse de que ya no está.</summary>
    [Fact]
    public async Task Al_retirarla_se_le_avisa_al_destino()
    {
        var transferencia = await ConTransferenciaAsync();

        await _entorno.Conversaciones.RetirarTransferenciaAsync(
            transferencia.TransferenciaId, EntornoDeReglas.TitularId);

        var avisos = await _entorno.Db.EventosSistema
            .Where(e => e.Tipo == TiposEvento.AnalistaNotificado)
            .Select(e => e.Payload)
            .ToListAsync();

        Assert.Contains(avisos, p => p.Contains($"\"AnalistaId\":{TerceroId}") && p.Contains("retiro"));
    }

    [Fact]
    public async Task El_destino_no_puede_retirar_lo_que_le_ofrecieron()
    {
        var transferencia = await ConTransferenciaAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _entorno.Conversaciones.RetirarTransferenciaAsync(transferencia.TransferenciaId, TerceroId));

        Assert.Contains("envio", error.Message);
        Assert.Equal(EstadoTransferencia.Pendiente, (await LeerAsync(transferencia.TransferenciaId)).Estado);
    }

    [Fact]
    public async Task No_se_puede_retirar_una_ya_respondida()
    {
        var transferencia = await ConTransferenciaAsync();

        await _entorno.Conversaciones.ResponderTransferenciaAsync(
            transferencia.TransferenciaId, TerceroId, aceptada: true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _entorno.Conversaciones.RetirarTransferenciaAsync(
                transferencia.TransferenciaId, EntornoDeReglas.TitularId));
    }

    [Fact]
    public async Task Una_transferencia_que_no_existe_no_se_puede_retirar()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _entorno.Conversaciones.RetirarTransferenciaAsync(9999, EntornoDeReglas.TitularId));
    }

    /// <summary>V21: retirada deja de bloquear, igual que vencida.</summary>
    [Fact]
    public async Task Retirada_no_bloquea_una_transferencia_nueva()
    {
        var transferencia = await ConTransferenciaAsync();

        await _entorno.Conversaciones.RetirarTransferenciaAsync(
            transferencia.TransferenciaId, EntornoDeReglas.TitularId);

        var otra = await _entorno.Conversaciones.TransferirAsync(
            transferencia.ConversacionId, EntornoDeReglas.TitularId, EntornoDeReglas.RespaldoId,
            urgente: false, null);

        Assert.Equal(EstadoTransferencia.Pendiente, otra.Estado);
    }

    [Fact]
    public async Task Lo_enviado_y_pendiente_se_puede_listar()
    {
        var transferencia = await ConTransferenciaAsync();

        var enviadas = await _entorno.Conversaciones.ListarTransferenciasEnviadasPendientesAsync(
            EntornoDeReglas.TitularId);

        Assert.Equal(transferencia.TransferenciaId, Assert.Single(enviadas).TransferenciaId);

        await _entorno.Conversaciones.RetirarTransferenciaAsync(
            transferencia.TransferenciaId, EntornoDeReglas.TitularId);

        Assert.Empty(await _entorno.Conversaciones.ListarTransferenciasEnviadasPendientesAsync(
            EntornoDeReglas.TitularId));
    }

    public void Dispose() => _entorno.Dispose();
}
