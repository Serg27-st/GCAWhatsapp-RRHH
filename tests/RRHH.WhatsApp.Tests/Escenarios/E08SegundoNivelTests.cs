using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Tests.Casos;

namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// E08 (FUN-05, A9, P3): escalar al respaldo no puede ser el final del camino. Si el respaldo
/// tampoco responde, Jefatura tiene que enterarse; si no, el hilo se queda esperando sin que nadie
/// lo sepa, que es lo que la auditoría encontró.
/// <para>
/// El plazo corre en horas hábiles: un escalamiento del viernes a la tarde no avisa el sábado.
/// </para>
/// </summary>
public class E08SegundoNivelTests : IDisposable
{
    private const int JefaturaId = 13;

    private readonly ArnesEscenario _arnes = new();

    private async Task ConJefaturaAsync()
    {
        _arnes.Entorno.Db.Analistas.Add(new Analista
        {
            AnalistaId = JefaturaId,
            Nombre = "Rosa Diaz",
            Email = "rosa@gca.pe",
            Rol = RolAnalista.Jefatura,
            Activo = true
        });

        await _arnes.Entorno.Db.SaveChangesAsync();
    }

    /// <summary>Deja el hilo escalado al respaldo, con el postulante esperando desde hace tres horas.</summary>
    private async Task<int> EscaladaAsync()
    {
        await _arnes.ConHorarioComercialAsync();
        await ConJefaturaAsync();

        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Boton("cuenta_7", "Alicorp"),
            new ConsumirOutbox(),
            new Despachar());

        // Tres horas hábiles de silencio del titular: el barrido escala al respaldo (Regla 2).
        await _arnes.ConversarAsync(new Avanzar(TimeSpan.FromHours(3)), new Barrido());

        var conversacion = await _arnes.ConversacionAsync();

        Assert.Equal(EstadoConversacion.Escalada, conversacion.Estado);
        Assert.Equal(EntornoDeReglas.RespaldoId, conversacion.AnalistaAtendiendoId);

        return conversacion.ConversacionId;
    }

    private async Task<IReadOnlyList<string>> AvisosAsync() =>
        [.. (await _arnes.Notificaciones()).Select(e => e.Payload)];

    [Fact]
    public async Task Si_el_respaldo_tampoco_responde_se_avisa_a_Jefatura()
    {
        await EscaladaAsync();
        var avisosAntes = (await AvisosAsync()).Count;

        // Dos horas hábiles más sin respuesta del respaldo: entra el segundo nivel (A9).
        await _arnes.ConversarAsync(new Avanzar(TimeSpan.FromHours(2)), new Barrido());

        var nuevos = (await AvisosAsync()).Skip(avisosAntes).ToList();

        Assert.Contains(nuevos, p => p.Contains($"\"analistaId\":{JefaturaId}"));
        Assert.Contains(nuevos, p => p.Contains($"\"analistaId\":{EntornoDeReglas.RespaldoId}"));
        Assert.Contains(nuevos, p => p.Contains($"\"analistaId\":{EntornoDeReglas.TitularId}"));

        Assert.NotNull((await _arnes.ConversacionAsync()).FechaAvisoSegundoNivel);
    }

    /// <summary>P4: el aviso sale una vez por escalamiento, no en cada vuelta del barrido.</summary>
    [Fact]
    public async Task El_aviso_a_Jefatura_no_se_repite_en_cada_barrido()
    {
        await EscaladaAsync();

        await _arnes.ConversarAsync(new Avanzar(TimeSpan.FromHours(2)), new Barrido());
        var despuesDelPrimero = (await AvisosAsync()).Count;

        await _arnes.ConversarAsync(new Avanzar(TimeSpan.FromHours(1)), new Barrido());

        Assert.Equal(despuesDelPrimero, (await AvisosAsync()).Count);
    }

    [Fact]
    public async Task Antes_del_plazo_no_se_avisa()
    {
        await EscaladaAsync();
        var avisosAntes = (await AvisosAsync()).Count;

        await _arnes.ConversarAsync(new Avanzar(TimeSpan.FromMinutes(30)), new Barrido());

        Assert.Equal(avisosAntes, (await AvisosAsync()).Count);
        Assert.Null((await _arnes.ConversacionAsync()).FechaAvisoSegundoNivel);
    }

    /// <summary>A9: el plazo es en horas hábiles; el fin de semana no cuenta.</summary>
    [Fact]
    public async Task El_fin_de_semana_no_dispara_el_segundo_nivel()
    {
        var id = await EscaladaAsync();

        // El viernes a las 17:00 de Lima quedan 60 minutos hábiles; el resto es fin de semana.
        var conversacion = await _arnes.Entorno.Db.Conversaciones.FirstAsync(c => c.ConversacionId == id);
        conversacion.FechaEscalamiento = new DateTime(2026, 9, 18, 22, 0, 0, DateTimeKind.Utc);
        await _arnes.Entorno.Db.SaveChangesAsync();

        // Sábado a la tarde: pasaron muchas horas de reloj, pero solo una hábil.
        _arnes.Entorno.Reloj.SetUtcNow(new DateTimeOffset(2026, 9, 19, 20, 0, 0, TimeSpan.Zero));
        await _arnes.ConversarAsync(new Barrido());

        Assert.Null((await _arnes.ConversacionAsync()).FechaAvisoSegundoNivel);
    }

    [Fact]
    public async Task Si_el_respaldo_responde_el_plazo_deja_de_correr()
    {
        var id = await EscaladaAsync();

        await _arnes.ConversarAsync(
            new RespuestaAnalista("Perdón la demora, ya lo veo.", EntornoDeReglas.RespaldoId));

        var avisosAntes = (await AvisosAsync()).Count;

        await _arnes.ConversarAsync(new Avanzar(TimeSpan.FromHours(2)), new Barrido());

        Assert.Equal(avisosAntes, (await AvisosAsync()).Count);
    }

    public void Dispose() => _arnes.Dispose();
}
