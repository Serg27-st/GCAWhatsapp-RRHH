using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Tests.Casos;

namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// E09 (FUN-07, A1, M5): una transferencia que nadie responde no puede bloquear el hilo para
/// siempre. Antes no vencía nunca: mientras estuviera pendiente, ninguna otra transferencia de esa
/// conversación era posible (V21).
/// </summary>
public class E09TransferenciaVenceTests : IDisposable
{
    private const int TerceroId = 12;

    private readonly ArnesEscenario _arnes = new();

    private async Task<(int ConversacionId, int TransferenciaId)> ConTransferenciaPendienteAsync()
    {
        await _arnes.ConHorarioComercialAsync();

        _arnes.Entorno.Db.Analistas.Add(new Analista
        {
            AnalistaId = TerceroId, Nombre = "Rosa Diaz", Email = "rosa@gca.pe", Activo = true
        });

        await _arnes.Entorno.Db.SaveChangesAsync();

        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Boton("cuenta_7", "Alicorp"),
            new ConsumirOutbox(),
            new Despachar());

        var conversacion = await _arnes.ConversacionAsync();

        var transferencia = await _arnes.Entorno.Conversaciones.TransferirAsync(
            conversacion.ConversacionId, EntornoDeReglas.TitularId, TerceroId, urgente: false, "¿La tomás?");

        return (conversacion.ConversacionId, transferencia.TransferenciaId);
    }

    private Task<Transferencia> LeerAsync(int transferenciaId) =>
        _arnes.Entorno.Db.Transferencias.AsNoTracking().FirstAsync(t => t.TransferenciaId == transferenciaId);

    [Fact]
    public async Task Una_transferencia_no_urgente_nace_con_vencimiento()
    {
        var (_, transferenciaId) = await ConTransferenciaPendienteAsync();

        var transferencia = await LeerAsync(transferenciaId);

        Assert.Equal(EstadoTransferencia.Pendiente, transferencia.Estado);
        Assert.NotNull(transferencia.FechaVencimiento);
    }

    [Fact]
    public async Task Sin_respuesta_vence_y_el_hilo_sigue_con_quien_la_envio()
    {
        var (_, transferenciaId) = await ConTransferenciaPendienteAsync();

        // El titular responde al postulante mientras espera la respuesta del destino: asi el hilo no
        // escala por la Regla 2 y lo unico que vence es la transferencia.
        await _arnes.ConversarAsync(
            new RespuestaAnalista("Te confirmo en un rato.", EntornoDeReglas.TitularId),
            new Avanzar(TimeSpan.FromHours(3)),
            new Barrido());

        var transferencia = await LeerAsync(transferenciaId);

        Assert.Equal(EstadoTransferencia.Vencida, transferencia.Estado);
        Assert.Equal(EntornoDeReglas.TitularId, (await _arnes.ConversacionAsync()).AnalistaAtendiendoId);
    }

    [Fact]
    public async Task Al_vencer_se_avisa_a_los_dos_analistas()
    {
        var (_, _) = await ConTransferenciaPendienteAsync();

        var avisosAntes = (await _arnes.Notificaciones()).Count;

        await _arnes.ConversarAsync(new Avanzar(TimeSpan.FromHours(3)), new Barrido());

        var nuevos = (await _arnes.Notificaciones()).Skip(avisosAntes).ToList();

        Assert.Contains(nuevos, e => e.Payload.Contains($"\"analistaId\":{EntornoDeReglas.TitularId}"));
        Assert.Contains(nuevos, e => e.Payload.Contains($"\"analistaId\":{TerceroId}"));
    }

    /// <summary>V21: vencida deja de bloquear, así que el hilo se puede volver a transferir.</summary>
    [Fact]
    public async Task Vencida_no_bloquea_una_transferencia_nueva()
    {
        var (conversacionId, _) = await ConTransferenciaPendienteAsync();

        await _arnes.ConversarAsync(new Avanzar(TimeSpan.FromHours(3)), new Barrido());

        var otra = await _arnes.Entorno.Conversaciones.TransferirAsync(
            conversacionId, EntornoDeReglas.TitularId, EntornoDeReglas.RespaldoId, urgente: false, null);

        Assert.Equal(EstadoTransferencia.Pendiente, otra.Estado);
    }

    [Fact]
    public async Task Antes_del_plazo_sigue_pendiente()
    {
        var (_, transferenciaId) = await ConTransferenciaPendienteAsync();

        await _arnes.ConversarAsync(new Avanzar(TimeSpan.FromMinutes(45)), new Barrido());

        Assert.Equal(EstadoTransferencia.Pendiente, (await LeerAsync(transferenciaId)).Estado);
    }

    /// <summary>A1: la urgente se aplica sin esperar respuesta, así que no tiene nada que vencer.</summary>
    [Fact]
    public async Task Una_transferencia_urgente_no_vence()
    {
        await _arnes.ConHorarioComercialAsync();

        _arnes.Entorno.Db.Analistas.Add(new Analista
        {
            AnalistaId = TerceroId, Nombre = "Rosa Diaz", Email = "rosa@gca.pe", Activo = true
        });

        await _arnes.Entorno.Db.SaveChangesAsync();

        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Boton("cuenta_7", "Alicorp"),
            new ConsumirOutbox(),
            new Despachar());

        var conversacion = await _arnes.ConversacionAsync();

        var transferencia = await _arnes.Entorno.Conversaciones.TransferirAsync(
            conversacion.ConversacionId, EntornoDeReglas.TitularId, TerceroId, urgente: true, null);

        Assert.Null(transferencia.FechaVencimiento);

        await _arnes.ConversarAsync(new Avanzar(TimeSpan.FromHours(5)), new Barrido());

        Assert.NotEqual(EstadoTransferencia.Vencida, (await LeerAsync(transferencia.TransferenciaId)).Estado);
    }

    /// <summary>FUN-07: transferirle a alguien ausente es dejar el hilo esperando a quien no está.</summary>
    [Fact]
    public async Task No_se_puede_transferir_a_un_analista_ausente()
    {
        await _arnes.ConHorarioComercialAsync();

        _arnes.Entorno.Db.Analistas.Add(new Analista
        {
            AnalistaId = TerceroId, Nombre = "Rosa Diaz", Email = "rosa@gca.pe", Activo = true
        });

        _arnes.Entorno.Db.Ausencias.Add(new Ausencia
        {
            AnalistaId = TerceroId,
            FechaInicio = _arnes.Entorno.Ahora.AddDays(-1),
            FechaFin = _arnes.Entorno.Ahora.AddDays(7),
            Motivo = "Vacaciones"
        });

        await _arnes.Entorno.Db.SaveChangesAsync();

        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Boton("cuenta_7", "Alicorp"),
            new ConsumirOutbox(),
            new Despachar());

        var conversacion = await _arnes.ConversacionAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _arnes.Entorno.Conversaciones.TransferirAsync(
                conversacion.ConversacionId, EntornoDeReglas.TitularId, TerceroId, urgente: false, null));
    }

    public void Dispose() => _arnes.Dispose();
}
