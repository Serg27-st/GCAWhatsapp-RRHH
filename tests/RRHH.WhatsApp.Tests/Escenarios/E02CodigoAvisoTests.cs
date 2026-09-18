using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;

namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// E02 (FUN-02, A6): quien llega por el enlace del aviso ya eligio la vacante. El bot le manda el
/// formulario directo, sin el menu de ~20 empresas que es justo lo que hace abandonar la postulacion.
/// </summary>
public class E02CodigoAvisoTests : IDisposable
{
    private const string Codigo = "K7M2QX";

    private readonly ArnesEscenario _arnes = new();

    private async Task ConCodigoAsync(EstadoHc estado = EstadoHc.Abierta)
    {
        var vacante = await _arnes.Entorno.Db.Hcs.FirstAsync();

        vacante.CodigoAviso = Codigo;
        vacante.Estado = estado;

        await _arnes.Entorno.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task El_codigo_del_aviso_manda_el_formulario_sin_menu()
    {
        await ConCodigoAsync();

        await _arnes.ConversarAsync(
            new Entrante($"Hola, postulo a {Codigo}"),
            new ConsumirOutbox(),
            new Despachar());

        Assert.Contains(_arnes.Enviados(), e => e.Detalle.Contains("completa esta ficha"));
        Assert.DoesNotContain(_arnes.Enviados(), e => e.Detalle.Contains("cuenta_"));

        // La cuenta de la vacante queda como contexto del hilo, con su analista.
        var conversacion = await _arnes.ConversacionAsync();

        Assert.Equal(EstadoConversacion.Activa, conversacion.Estado);
        Assert.NotNull(conversacion.CuentaContextoId);
    }

    /// <summary>El aviso puede seguir circulando despues de cubrir la vacante: la R20 lo resuelve.</summary>
    [Fact]
    public async Task El_codigo_de_una_vacante_cerrada_da_el_aviso_de_la_Regla_20()
    {
        await ConCodigoAsync(EstadoHc.Cerrada);

        await _arnes.ConversarAsync(
            new Entrante($"Hola, postulo a {Codigo}"),
            new ConsumirOutbox(),
            new Despachar());

        Assert.Contains(_arnes.Enviados(), e => e.Detalle.Contains("ya fue cubierta"));
        Assert.DoesNotContain(_arnes.Enviados(), e => e.Detalle.Contains("completa esta ficha"));
    }

    /// <summary>FUN-02: escribir el nombre de la empresa tambien alcanza; el menu es el ultimo recurso.</summary>
    [Fact]
    public async Task El_nombre_de_la_empresa_tambien_identifica_la_cuenta()
    {
        await _arnes.ConversarAsync(
            new Entrante("alicorp"),
            new ConsumirOutbox(),
            new Despachar());

        Assert.Contains(_arnes.Enviados(), e => e.Detalle.Contains("completa esta ficha"));
        Assert.Equal(EstadoConversacion.Activa, (await _arnes.ConversacionAsync()).Estado);
    }

    [Fact]
    public async Task Un_texto_que_no_es_codigo_ni_empresa_sigue_dando_el_menu()
    {
        await ConCodigoAsync();

        await _arnes.ConversarAsync(
            new Entrante("Hola, hay trabajo?"),
            new ConsumirOutbox(),
            new Despachar());

        Assert.Contains(_arnes.Enviados(), e => e.Detalle.Contains("cuenta_"));
        Assert.Equal(EstadoConversacion.EnMenuBot, (await _arnes.ConversacionAsync()).Estado);
    }

    public void Dispose() => _arnes.Dispose();
}
