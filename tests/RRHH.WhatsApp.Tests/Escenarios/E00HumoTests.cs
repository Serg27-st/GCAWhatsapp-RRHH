using RRHH.WhatsApp.Domain.Enums;

namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// Escenario de humo del arnés (T1.02): si esto se rompe, fallan todos los E## por la misma causa y
/// conviene verlo aislado.
/// </summary>
public class E00HumoTests : IDisposable
{
    private readonly ArnesEscenario _arnes = new();

    [Fact]
    public async Task Hola_muestra_el_menu_de_empresas()
    {
        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Despachar());

        var menu = Assert.Single(_arnes.Enviados());

        Assert.Equal("botones", menu.Tipo);
        Assert.Contains("cuenta_7", menu.Detalle);
    }

    [Fact]
    public async Task Elegir_la_empresa_deja_la_conversacion_con_su_titular()
    {
        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Avanzar(TimeSpan.FromMinutes(1)),
            new Boton("cuenta_7", "Alicorp"),
            new ConsumirOutbox(),
            new Despachar());

        var conversacion = await _arnes.ConversacionAsync();

        Assert.Equal(EstadoConversacion.Activa, conversacion.Estado);
        Assert.Equal(Casos.EntornoDeReglas.TitularId, conversacion.AnalistaAtendiendoId);

        // El timestamp del payload sale del reloj simulado: la ventana nace abierta en el escenario.
        Assert.Equal(_arnes.Entorno.Ahora, conversacion.FechaUltimoMensajeEntrante);
    }

    public void Dispose() => _arnes.Dispose();
}
