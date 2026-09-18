using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Tests.Casos;

namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// E10 (COR-08, AL4): la ausencia del titular no pisa una conversacion que ya atiende otra persona.
/// <para>
/// La R14 reasignaba al respaldo en cada mensaje, asi que una transferencia aceptada se deshacia
/// sola con el siguiente entrante y el postulante volvia con quien no correspondia.
/// </para>
/// </summary>
public class E10AusenciaTransferenciaTests : IDisposable
{
    private const int TerceroId = 12;

    private readonly ArnesEscenario _arnes = new();

    private async Task ConTitularAusenteAsync()
    {
        _arnes.Entorno.Db.Ausencias.Add(new Ausencia
        {
            AnalistaId = EntornoDeReglas.TitularId,
            FechaInicio = _arnes.Entorno.Ahora.AddDays(-1),
            FechaFin = _arnes.Entorno.Ahora.AddDays(7),
            Motivo = "Vacaciones"
        });

        await _arnes.Entorno.Db.SaveChangesAsync();
    }

    private async Task ConTerceroAsync()
    {
        _arnes.Entorno.Db.Analistas.Add(new Analista
        {
            AnalistaId = TerceroId, Nombre = "Rosa Diaz", Email = "rosa@gca.pe", Activo = true
        });

        await _arnes.Entorno.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Una_conversacion_transferida_no_vuelve_al_respaldo_cuando_el_titular_se_ausenta()
    {
        await ConTerceroAsync();

        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Boton("cuenta_7", "Alicorp"),
            new ConsumirOutbox(),
            new Despachar());

        var conversacion = await _arnes.ConversacionAsync();

        var transferencia = await _arnes.Entorno.Conversaciones.TransferirAsync(
            conversacion.ConversacionId, EntornoDeReglas.TitularId, TerceroId, urgente: false, "Cubre tu");

        await _arnes.Entorno.Conversaciones.ResponderTransferenciaAsync(
            transferencia.TransferenciaId, TerceroId, aceptada: true);

        await ConTitularAusenteAsync();

        await _arnes.ConversarAsync(
            new Entrante("Una consulta mas"),
            new ConsumirOutbox(),
            new Despachar());

        Assert.Equal(TerceroId, (await _arnes.ConversacionAsync()).AnalistaAtendiendoId);
    }

    [Fact]
    public async Task Sin_nadie_atendiendo_la_ausencia_si_enruta_al_respaldo()
    {
        await ConTitularAusenteAsync();

        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Boton("cuenta_7", "Alicorp"),
            new ConsumirOutbox(),
            new Despachar());

        Assert.Equal(EntornoDeReglas.RespaldoId, (await _arnes.ConversacionAsync()).AnalistaAtendiendoId);
    }

    /// <summary>Sin respaldo configurado el hilo va a «Sin clasificar», pero con su plazo (FUN-06).</summary>
    [Fact]
    public async Task Sin_respaldo_la_conversacion_queda_en_sin_clasificar_con_plazo()
    {
        var respaldo = await _arnes.Entorno.Db.AnalistaCuentas.FirstAsync(a => a.EsBackup);
        _arnes.Entorno.Db.AnalistaCuentas.Remove(respaldo);
        await _arnes.Entorno.Db.SaveChangesAsync();

        await ConTitularAusenteAsync();

        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Boton("cuenta_7", "Alicorp"),
            new ConsumirOutbox(),
            new Despachar());

        var conversacion = await _arnes.ConversacionAsync();

        Assert.Equal(EstadoConversacion.PendienteClasificar, conversacion.Estado);
        Assert.NotNull(conversacion.FechaPendienteDesde);
    }

    public void Dispose() => _arnes.Dispose();
}
