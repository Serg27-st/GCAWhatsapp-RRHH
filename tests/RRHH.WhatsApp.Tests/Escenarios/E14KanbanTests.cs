using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Tests.Casos;

namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// E14, parte de estado (COR-11, AL7, P4): la columna del tablero es el desenlace de la postulacion,
/// y arrastrarla de vuelta lo revierte.
/// <para>
/// El desenlace se decidia por el nombre de la columna (<c>StartsWith("Descartado")</c>), asi que
/// renombrarla lo rompia; y sacar la tarjeta de una columna final dejaba la postulacion descartada
/// para siempre, aunque volviera a Entrevista.
/// </para>
/// </summary>
public class E14KanbanTests : IDisposable
{
    private readonly ArnesEscenario _arnes = new();

    private Task<int> DescartesPublicadosAsync() =>
        _arnes.Entorno.Db.EventosSistema.CountAsync(e => e.Tipo == TiposEvento.PostulacionDescartada);

    private async Task<int> EtapaAsync(string nombre) =>
        (await _arnes.Entorno.Db.EtapasKanban.AsNoTracking().FirstAsync(e => e.Nombre == nombre)).EtapaId;

    private async Task<Domain.Entidades.Postulacion> PostulacionAsync() =>
        await _arnes.Entorno.Db.Postulaciones.AsNoTracking().FirstAsync();

    /// <summary>Deja una postulacion creada por el circuito real, en la primera etapa.</summary>
    private async Task<int> ConPostulacionAsync()
    {
        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Boton("cuenta_7", "Alicorp"),
            new ConsumirOutbox(),
            new Despachar(),
            new Formulario(),
            new ConsumirOutbox(),
            new Despachar());

        return (await PostulacionAsync()).PostulacionId;
    }

    private Task MoverAsync(int postulacionId, int etapaId) =>
        _arnes.Entorno.Bandeja.MoverEtapaAsync(postulacionId, etapaId, EntornoDeReglas.TitularId);

    [Fact]
    public async Task Sacar_la_tarjeta_de_Descartado_devuelve_la_postulacion_a_EnProceso()
    {
        var postulacionId = await ConPostulacionAsync();

        await MoverAsync(postulacionId, await EtapaAsync("Descartado"));
        Assert.Equal(EstadoPostulacion.Descartado, (await PostulacionAsync()).Estado);

        await MoverAsync(postulacionId, await EtapaAsync("Entrevista"));

        Assert.Equal(EstadoPostulacion.EnProceso, (await PostulacionAsync()).Estado);
    }

    /// <summary>P4: el cierre de cortesia se decide una vez; volver a descartar no lo dispara de nuevo.</summary>
    [Fact]
    public async Task Volver_a_Descartado_no_publica_un_segundo_descarte_si_ya_lo_estaba()
    {
        var postulacionId = await ConPostulacionAsync();
        var descartado = await EtapaAsync("Descartado");

        await MoverAsync(postulacionId, descartado);
        Assert.Equal(1, await DescartesPublicadosAsync());

        await MoverAsync(postulacionId, descartado);
        Assert.Equal(1, await DescartesPublicadosAsync());
    }

    [Fact]
    public async Task Reconsiderar_y_volver_a_descartar_si_publica_el_descarte_nuevo()
    {
        var postulacionId = await ConPostulacionAsync();

        await MoverAsync(postulacionId, await EtapaAsync("Descartado"));
        await MoverAsync(postulacionId, await EtapaAsync("Entrevista"));
        await MoverAsync(postulacionId, await EtapaAsync("Descartado"));

        Assert.Equal(2, await DescartesPublicadosAsync());
    }

    [Fact]
    public async Task Mover_a_Contratado_cierra_la_postulacion_sin_publicar_descarte()
    {
        var postulacionId = await ConPostulacionAsync();

        await MoverAsync(postulacionId, await EtapaAsync("Contratado"));

        Assert.Equal(EstadoPostulacion.Contratado, (await PostulacionAsync()).Estado);
        Assert.Equal(0, await DescartesPublicadosAsync());
    }


    /// <summary>
    /// FUN-10 (P4): la despedida sale una sola vez por postulación. Reconsiderar y volver a descartar
    /// publica el descarte de nuevo —es un hecho nuevo—, pero el sello impide un segundo mensaje.
    /// </summary>
    [Fact]
    public async Task Descartar_dos_veces_no_despide_dos_veces()
    {
        var postulacionId = await ConPostulacionAsync();

        await MoverAsync(postulacionId, await EtapaAsync("Descartado"));
        await _arnes.ConversarAsync(new ConsumirOutbox(), new Despachar());

        await MoverAsync(postulacionId, await EtapaAsync("Entrevista"));
        await MoverAsync(postulacionId, await EtapaAsync("Descartado"));
        await _arnes.ConversarAsync(new ConsumirOutbox(), new Despachar());

        Assert.Equal(1, _arnes.Enviados().Count(e => e.Detalle.Contains("Agradecemos tu interes")));
        Assert.Equal(2, await DescartesPublicadosAsync());
    }

    public void Dispose() => _arnes.Dispose();
}
