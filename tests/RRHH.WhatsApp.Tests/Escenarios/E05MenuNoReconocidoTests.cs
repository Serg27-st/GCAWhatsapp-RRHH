using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;

namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// E05 parte 1 (COR-06, AL2): el bot reintenta el menu una vez y recien despues deriva a «Sin
/// clasificar», con su plazo.
/// <para>
/// El contador vivia en el conteo de mensajes entrantes del hilo, asi que un postulante conocido
/// —con historia— entraba ya por encima del umbral: su siguiente texto iba directo a la bandeja
/// general sin ver nunca el menu.
/// </para>
/// </summary>
public class E05MenuNoReconocidoTests : IDisposable
{
    private readonly ArnesEscenario _arnes = new();

    private int Menus() => _arnes.Enviados().Count(e => e.Detalle.Contains("cuenta_"));

    [Fact]
    public async Task Hola_da_el_menu_xx_el_reintento_y_yy_deriva_con_plazo()
    {
        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Despachar());

        Assert.Equal(1, Menus());

        await _arnes.ConversarAsync(
            new Entrante("xx"),
            new ConsumirOutbox(),
            new Despachar());

        Assert.Equal(2, Menus());
        Assert.Equal(EstadoConversacion.EnMenuBot, await _arnes.EstadoConversacion());

        await _arnes.ConversarAsync(
            new Entrante("yy"),
            new ConsumirOutbox(),
            new Despachar());

        var conversacion = await _arnes.ConversacionAsync();

        Assert.Equal(EstadoConversacion.PendienteClasificar, conversacion.Estado);
        Assert.NotNull(conversacion.FechaPendienteDesde);
        Assert.Equal(2, Menus());
    }

    /// <summary>
    /// AL2: un hilo con historia al que se le limpio el contexto vuelve a empezar el menu. Antes el
    /// contador eran «todos los entrantes menos uno», asi que estas 30 preguntas lo mandaban derecho
    /// a la bandeja general sin ofrecerle nada.
    /// </summary>
    [Fact]
    public async Task Un_hilo_con_historia_vuelve_a_ver_el_menu_y_no_la_bandeja_general()
    {
        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Boton("cuenta_7", "Alicorp"),
            new ConsumirOutbox(),
            new Despachar());

        // Treinta mensajes de una conversacion normal con su analista.
        for (var i = 0; i < 30; i++)
        {
            await _arnes.ConversarAsync(
                new Entrante($"Consulta {i}"),
                new ConsumirOutbox(),
                new Despachar());
        }

        await LimpiarContextoAsync();

        await _arnes.ConversarAsync(
            new Entrante("zz"),
            new ConsumirOutbox(),
            new Despachar());

        Assert.Equal(EstadoConversacion.EnMenuBot, await _arnes.EstadoConversacion());
        Assert.Contains(_arnes.Enviados().TakeLast(1), e => e.Detalle.Contains("cuenta_"));
    }


    /// <summary>
    /// E05 parte 2 (FUN-06, P3): derivar no alcanza. Si nadie toma el hilo en el plazo, Jefatura se
    /// entera: la bandeja general la ven todos, que es como decir que no la mira nadie.
    /// </summary>
    [Fact]
    public async Task Derivada_y_sin_que_nadie_la_tome_se_le_avisa_a_Jefatura()
    {
        await _arnes.ConHorarioComercialAsync();

        _arnes.Entorno.Db.Analistas.Add(new Analista
        {
            AnalistaId = 13, Nombre = "Rosa Diaz", Email = "rosa@gca.pe", Rol = RolAnalista.Jefatura, Activo = true
        });

        await _arnes.Entorno.Db.SaveChangesAsync();

        await _arnes.ConversarAsync(
            new Entrante("Hola"), new ConsumirOutbox(), new Despachar(),
            new Entrante("xx"), new ConsumirOutbox(), new Despachar(),
            new Entrante("yy"), new ConsumirOutbox(), new Despachar());

        Assert.Equal(EstadoConversacion.PendienteClasificar, await _arnes.EstadoConversacion());

        // Dos horas hábiles en la bandeja general sin que ningún analista la tome.
        await _arnes.ConversarAsync(new Avanzar(TimeSpan.FromHours(2)), new Barrido());

        Assert.Contains(await _arnes.Notificaciones(), e => e.Payload.Contains("\"analistaId\":13"));
        Assert.NotNull((await _arnes.ConversacionAsync()).FechaAvisoPendiente);
    }
    /// <summary>Lo que hace la Regla 9 cuando el postulante reaparece tras varios dias.</summary>
    private async Task LimpiarContextoAsync()
    {
        var conversacion = await _arnes.ConversacionAsync();

        await _arnes.Entorno.Conversaciones.LimpiarCuentaContextoAsync(
            conversacion.ConversacionId, "Prueba: el bot vuelve a preguntar la empresa.");
    }

    public void Dispose() => _arnes.Dispose();
}
