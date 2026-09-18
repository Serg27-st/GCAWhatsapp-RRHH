using RRHH.WhatsApp.Domain.Enums;

namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// E01 (ARQ-05, V30): el primer mensaje de alguien que no eligió empresa lo atiende el bot, y no aparece
/// en «Sin clasificar» mientras lo atiende. Antes el hilo nacía en <c>PendienteClasificar</c> y la
/// bandeja general se llenaba de conversaciones que el bot todavía estaba resolviendo.
/// <para>
/// Esta es la parte de estado (T2.05). El aviso fuera de horario del mismo escenario llega con T3.02.
/// </para>
/// </summary>
public class E01PrimerMensajeTests : IDisposable
{
    private readonly ArnesEscenario _arnes = new();

    [Fact]
    public async Task El_primer_mensaje_queda_en_el_menu_del_bot_y_fuera_de_sin_clasificar()
    {
        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Despachar(),
            new Avanzar(TimeSpan.FromMinutes(2)),
            new Entrante("¿Hay trabajo?"),
            new ConsumirOutbox(),
            new Despachar());

        Assert.Equal(EstadoConversacion.EnMenuBot, await _arnes.EstadoConversacion());
        Assert.Empty(await _arnes.Entorno.Conversaciones.ListarPendientesClasificarAsync());
    }

    [Fact]
    public async Task Elegir_la_empresa_saca_la_conversacion_del_menu_del_bot()
    {
        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Boton("cuenta_7", "Alicorp"),
            new ConsumirOutbox());

        Assert.Equal(EstadoConversacion.Activa, await _arnes.EstadoConversacion());
    }

    public void Dispose() => _arnes.Dispose();
}
