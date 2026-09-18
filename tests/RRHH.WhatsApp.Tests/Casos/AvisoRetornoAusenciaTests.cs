using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Infrastructure.Servicios;
using RRHH.WhatsApp.Tests.Escenarios;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// FUN-12 (A10): quien vuelve de vacaciones tiene que saber qué quedó con su respaldo. Sin el aviso,
/// el titular retoma su bandeja sin enterarse de que hay conversaciones nuevas de sus cuentas
/// atendidas por otra persona.
/// </summary>
public class AvisoRetornoAusenciaTests : IDisposable
{
    private readonly ArnesEscenario _arnes = new();

    private AvisoRetornoAusencia Aviso()
    {
        var db = _arnes.Entorno.Db;
        var reloj = _arnes.Entorno.Reloj;

        return new AvisoRetornoAusencia(
            new AusenciaService(db, reloj),
            _arnes.Entorno.Cuentas,
            _arnes.Entorno.Conversaciones,
            _arnes.Entorno.Eventos,
            _arnes.Entorno.Unidad,
            reloj,
            NullLogger<AvisoRetornoAusencia>.Instance);
    }

    private async Task<Ausencia> TitularDeVacacionesAsync(int dias = 7)
    {
        var ausencia = new Ausencia
        {
            AnalistaId = EntornoDeReglas.TitularId,
            FechaInicio = _arnes.Entorno.Ahora.AddMinutes(-1),
            FechaFin = _arnes.Entorno.Ahora.AddDays(dias),
            Motivo = "Vacaciones"
        };

        _arnes.Entorno.Db.Ausencias.Add(ausencia);
        await _arnes.Entorno.Db.SaveChangesAsync();

        return ausencia;
    }

    private async Task<IReadOnlyList<string>> AvisosAlTitularAsync() =>
        [.. (await _arnes.Notificaciones())
            .Select(e => e.Payload)
            .Where(p => p.Contains($"\"analistaId\":{EntornoDeReglas.TitularId}") && p.Contains("ausencia"))];

    [Fact]
    public async Task Al_volver_se_le_cuenta_al_titular_lo_que_quedo_con_su_respaldo()
    {
        var ausencia = await TitularDeVacacionesAsync();

        // Mientras no está, entra un postulante nuevo a su cuenta: lo toma el respaldo (Regla 14).
        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Boton("cuenta_7", "Alicorp"),
            new ConsumirOutbox(),
            new Despachar());

        Assert.Equal(EntornoDeReglas.RespaldoId, (await _arnes.ConversacionAsync()).AnalistaAtendiendoId);

        await _arnes.ConversarAsync(new Avanzar(TimeSpan.FromDays(8)));

        await Aviso().ProcesarAsync();

        var aviso = Assert.Single(await AvisosAlTitularAsync());
        Assert.Contains("1 conversacion", aviso);
        Assert.Contains("Luis Vega", aviso);

        var sellada = await _arnes.Entorno.Db.Ausencias.AsNoTracking()
            .FirstAsync(a => a.AusenciaId == ausencia.AusenciaId);

        Assert.NotNull(sellada.FechaAvisoRetorno);
    }

    /// <summary>P4: un aviso por ausencia, aunque el barrido pase muchas veces.</summary>
    [Fact]
    public async Task El_aviso_no_se_repite()
    {
        await TitularDeVacacionesAsync();

        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Boton("cuenta_7", "Alicorp"),
            new ConsumirOutbox(),
            new Avanzar(TimeSpan.FromDays(8)));

        await Aviso().ProcesarAsync();
        await Aviso().ProcesarAsync();

        Assert.Single(await AvisosAlTitularAsync());
    }

    [Fact]
    public async Task Mientras_la_ausencia_sigue_no_se_avisa()
    {
        await TitularDeVacacionesAsync(dias: 7);

        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Boton("cuenta_7", "Alicorp"),
            new ConsumirOutbox(),
            new Avanzar(TimeSpan.FromDays(3)));

        await Aviso().ProcesarAsync();

        Assert.Empty(await AvisosAlTitularAsync());
    }

    /// <summary>Si no quedó nada con el respaldo, el aviso sería ruido: se sella sin mandar nada.</summary>
    [Fact]
    public async Task Sin_conversaciones_nuevas_no_se_manda_nada_pero_se_sella()
    {
        var ausencia = await TitularDeVacacionesAsync();

        await _arnes.ConversarAsync(new Avanzar(TimeSpan.FromDays(8)));

        await Aviso().ProcesarAsync();

        Assert.Empty(await AvisosAlTitularAsync());

        var sellada = await _arnes.Entorno.Db.Ausencias.AsNoTracking()
            .FirstAsync(a => a.AusenciaId == ausencia.AusenciaId);

        Assert.NotNull(sellada.FechaAvisoRetorno);
    }

    public void Dispose() => _arnes.Dispose();
}
