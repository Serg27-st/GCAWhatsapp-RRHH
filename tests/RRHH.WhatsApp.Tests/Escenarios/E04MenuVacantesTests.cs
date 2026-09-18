using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Tests.Casos;

namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// E04 parte 2 (COR-02, C2, P4): el menu de vacantes se muestra una sola vez. Mientras el bot lo
/// reenviaba en cada mensaje, el postulante que ya habia elegido —y hasta completado el formulario—
/// seguia recibiendo el mismo menu, que es el ruido que termina en reportes de spam.
/// </summary>
public class E04MenuVacantesTests : IDisposable
{
    private readonly ArnesEscenario _arnes = new();

    /// <summary>Los menus de vacantes son los que ofrecen botones <c>hc_</c>.</summary>
    private int MenusDeVacantes() =>
        _arnes.Enviados().Count(e => e.Tipo is "botones" or "lista" && e.Detalle.Contains("hc_"));

    private async Task SegundaVacanteAsync()
    {
        _arnes.Entorno.Db.Hcs.Add(new Hc
        {
            HcId = 2,
            CuentaId = EntornoDeReglas.CuentaId,
            Titulo = "Almacenero",
            UrlJobForms = "https://forms.gle/almacenero",
            Estado = EstadoHc.Abierta,
            FechaCreacion = _arnes.Entorno.Ahora
        });

        await _arnes.Entorno.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Con_dos_vacantes_el_menu_sale_una_sola_vez_en_todo_el_hilo()
    {
        await SegundaVacanteAsync();

        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Despachar(),
            new Boton("cuenta_7", "Alicorp"),
            new ConsumirOutbox(),
            new Despachar(),
            new Boton("hc_1", "Operario de produccion"),
            new ConsumirOutbox(),
            new Despachar(),
            new Formulario(),
            new ConsumirOutbox(),
            new Despachar(),
            new Entrante("Cuando me llaman?"),
            new Entrante("Sigo interesado"),
            new Entrante("Hay novedades?"),
            new ConsumirOutbox(),
            new Despachar());

        Assert.Equal(1, MenusDeVacantes());
    }

    /// <summary>
    /// La cuenta abre otra vacante mientras el postulante ya esta en proceso: eso es trabajo del
    /// analista, no motivo para interrumpirlo con un menu (C2).
    /// </summary>
    [Fact]
    public async Task Una_vacante_nueva_no_le_abre_el_menu_a_quien_ya_esta_en_proceso()
    {
        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Despachar(),
            new Boton("cuenta_7", "Alicorp"),
            new ConsumirOutbox(),
            new Despachar(),
            new Formulario(),
            new ConsumirOutbox(),
            new Despachar());

        await SegundaVacanteAsync();

        await _arnes.ConversarAsync(
            new Entrante("Hay novedades?"),
            new ConsumirOutbox(),
            new Despachar());

        Assert.Equal(0, MenusDeVacantes());
    }

    public void Dispose() => _arnes.Dispose();
}
