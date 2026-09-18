using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Tests.Casos;

namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// E11 y E12 (COR-07, AL3, A13): volver a escribir a los dias no es empezar de cero.
/// <para>
/// Quien tiene un proceso vivo sigue con su analista; quien no lo tiene vuelve al menu. Antes la
/// repregunta miraba solo el estado: a quien estaba <c>EnProceso</c> se le limpiaba el contexto —y
/// con el, su analista— y un <c>Descartado</c> en cualquier cuenta bloqueaba el menu para siempre.
/// </para>
/// </summary>
public class E11E12RepreguntaTests : IDisposable
{
    private readonly ArnesEscenario _arnes = new();

    private static readonly TimeSpan CincoDias = TimeSpan.FromDays(5);

    private int Menus() => _arnes.Enviados().Count(e => e.Detalle.Contains("cuenta_"));

    /// <summary>Deja al postulante identificado, con su postulacion creada y su analista asignado.</summary>
    private async Task EnProcesoAsync() => await _arnes.ConversarAsync(
        new Entrante("Hola"),
        new ConsumirOutbox(),
        new Despachar(),
        new Boton("cuenta_7", "Alicorp"),
        new ConsumirOutbox(),
        new Despachar(),
        new Formulario(),
        new ConsumirOutbox(),
        new Despachar());

    [Fact]
    public async Task E11_quien_esta_en_proceso_vuelve_a_los_cinco_dias_y_sigue_con_su_analista()
    {
        await EnProcesoAsync();
        var menusAntes = Menus();

        await _arnes.ConversarAsync(
            new Avanzar(CincoDias),
            new Entrante("Hay novedades de mi postulacion?"),
            new ConsumirOutbox(),
            new Despachar());

        var conversacion = await _arnes.ConversacionAsync();

        Assert.Equal(EstadoConversacion.Activa, conversacion.Estado);
        Assert.Equal(EntornoDeReglas.TitularId, conversacion.AnalistaAtendiendoId);
        Assert.NotNull(conversacion.CuentaContextoId);
        Assert.Equal(menusAntes, Menus());
    }

    [Fact]
    public async Task E12_quien_fue_descartado_vuelve_a_los_cinco_dias_y_recibe_el_menu()
    {
        await EnProcesoAsync();
        await DescartarAsync();

        var menusAntes = Menus();

        await _arnes.ConversarAsync(
            new Avanzar(CincoDias),
            new Entrante("Hola de nuevo"),
            new ConsumirOutbox(),
            new Despachar());

        var conversacion = await _arnes.ConversacionAsync();

        Assert.Equal(EstadoConversacion.EnMenuBot, conversacion.Estado);
        Assert.Null(conversacion.CuentaContextoId);
        Assert.Equal(menusAntes + 1, Menus());
    }

    /// <summary>Volver al dia siguiente no dispara nada: la repregunta es por inactividad larga.</summary>
    [Fact]
    public async Task Volver_al_dia_siguiente_no_repregunta()
    {
        await EnProcesoAsync();
        await DescartarAsync();

        var menusAntes = Menus();

        await _arnes.ConversarAsync(
            new Avanzar(TimeSpan.FromDays(1)),
            new Entrante("Una consulta"),
            new ConsumirOutbox(),
            new Despachar());

        Assert.Equal(menusAntes, Menus());
    }

    private async Task DescartarAsync()
    {
        var postulacion = await _arnes.Entorno.Db.Postulaciones.FirstAsync();
        postulacion.Estado = EstadoPostulacion.Descartado;

        await _arnes.Entorno.Db.SaveChangesAsync();
    }

    public void Dispose() => _arnes.Dispose();
}
