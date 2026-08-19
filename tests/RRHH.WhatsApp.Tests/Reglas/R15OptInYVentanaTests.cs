using RRHH.WhatsApp.Application.Reglas.Implementaciones;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Tests.Reglas;

/// <summary>
/// Regla 15. Es la regla que ataca la causa del bloqueo original, asi que se prueba tanto lo que
/// bloquea como lo que deja pasar.
/// </summary>
public class R15OptInYVentanaTests
{
    private readonly R15OptInYVentana _regla = new();

    [Fact]
    public async Task Bloquea_el_envio_cuando_no_hay_optin_registrado()
    {
        var ctx = new ConstructorContexto()
            .Disparador(TipoDisparador.EnvioSaliente)
            .ConConversacion(optIn: null)
            .Construir();

        var resultado = await _regla.EvaluarAsync(ctx);

        Assert.Contains(resultado.Acciones, a => a is BloquearEnvio);
        Assert.True(resultado.DetenerEvaluacion);
    }

    [Fact]
    public async Task Permite_texto_libre_dentro_de_la_ventana_de_24h()
    {
        var ctx = new ConstructorContexto()
            .Disparador(TipoDisparador.EnvioSaliente)
            .ConConversacion(
                optIn: ConstructorContexto.Ahora.AddDays(-5),
                ultimoEntrante: ConstructorContexto.Ahora.AddHours(-3))
            .Construir();

        var resultado = await _regla.EvaluarAsync(ctx);

        Assert.Empty(resultado.Acciones);
        Assert.False(resultado.DetenerEvaluacion);
    }

    [Fact]
    public async Task Exige_plantilla_cuando_la_ventana_de_24h_ya_cerro()
    {
        var ctx = new ConstructorContexto()
            .Disparador(TipoDisparador.EnvioSaliente)
            .ConConversacion(
                optIn: ConstructorContexto.Ahora.AddDays(-5),
                ultimoEntrante: ConstructorContexto.Ahora.AddHours(-25))
            .Construir();

        var resultado = await _regla.EvaluarAsync(ctx);

        var evento = Assert.Single(resultado.Acciones.OfType<PublicarEvento>());
        Assert.Equal("EnvioRequierePlantilla", evento.Tipo);
        Assert.DoesNotContain(resultado.Acciones, a => a is BloquearEnvio);
    }

    [Fact]
    public async Task El_optin_no_alcanza_por_si_solo_si_la_ventana_cerro()
    {
        // Caso limite: hay consentimiento historico pero el ultimo mensaje entrante es viejo.
        // No se bloquea, pero tampoco se permite texto libre.
        var ctx = new ConstructorContexto()
            .Disparador(TipoDisparador.EnvioSaliente)
            .ConConversacion(
                optIn: ConstructorContexto.Ahora.AddMonths(-2),
                ultimoEntrante: ConstructorContexto.Ahora.AddHours(-24).AddMinutes(-1))
            .Construir();

        var resultado = await _regla.EvaluarAsync(ctx);

        Assert.False(ctx.VentanaServicioAbierta);
        Assert.True(ctx.TieneOptIn);
        Assert.Contains(resultado.Acciones, a => a is PublicarEvento);
    }

    [Fact]
    public void No_aplica_a_los_mensajes_entrantes()
    {
        var ctx = new ConstructorContexto()
            .Disparador(TipoDisparador.MensajeEntrante)
            .ConConversacion()
            .Construir();

        Assert.False(_regla.Aplica(ctx));
    }
}
