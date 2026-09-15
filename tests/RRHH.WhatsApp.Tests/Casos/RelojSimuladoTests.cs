using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Tests.Proveedores;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// T0.10 (ARQ-01, ARQ-12): el circuito completo corre contra un reloj que la prueba controla. Sin
/// esto las reglas por tiempo —las 2 horas de la R2, las 24 y 48 de la R9, los 90 días de la R16—
/// solo se podían probar retocando fechas en la base, y así se escaparon C1 y AL10.
/// </summary>
public class RelojSimuladoTests : IDisposable
{
    private readonly EntornoDeReglas _entorno = new();

    [Fact]
    public void El_entorno_arranca_un_lunes_a_las_10_hora_de_Lima()
    {
        // Dentro del horario laboral a propósito: una prueba que no va sobre la Regla 3 no debería
        // depender de a qué hora la corre quien la corre.
        var inicio = _entorno.Reloj.GetUtcNow().UtcDateTime;
        var lima = inicio.AddHours(-5);

        Assert.Equal(DayOfWeek.Monday, lima.DayOfWeek);
        Assert.Equal(10, lima.Hour);
    }

    [Fact]
    public async Task Lo_que_registran_los_servicios_sigue_al_reloj_simulado()
    {
        await _entorno.IngresarAsync(PayloadsDePrueba.MensajeDeTexto);
        var recien = await _entorno.Db.Conversaciones.AsNoTracking().FirstAsync();

        Assert.Equal(_entorno.Reloj.GetUtcNow().UtcDateTime, recien.FechaUltimoMensajeEntrante);

        await _entorno.AvanzarAsync(TimeSpan.FromHours(2));
        await _entorno.RefrescarActividadAsync(recien.ConversacionId);

        var refrescada = await _entorno.Db.Conversaciones.AsNoTracking().FirstAsync();

        Assert.Equal(TimeSpan.FromHours(2), refrescada.FechaUltimaActividad - recien.FechaUltimaActividad);
    }

    public void Dispose() => _entorno.Dispose();
}
