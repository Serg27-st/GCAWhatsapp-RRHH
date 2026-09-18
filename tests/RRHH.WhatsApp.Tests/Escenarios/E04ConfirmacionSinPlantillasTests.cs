using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;

namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// E04 parte 1 (COR-03, C3, P1): el bot no puede quedarse mudo esperando a Meta. Las seis plantillas
/// nacen inactivas, asi que mientras cada mensaje del bot era una plantilla, quien completaba el
/// formulario no recibia ninguna confirmacion.
/// <para>
/// Dentro de la ventana de 24h el texto libre es legal y no depende de aprobacion; fuera de ella si,
/// y entonces lo que no sale tampoco se sella: queda pendiente hasta que la plantilla se apruebe.
/// </para>
/// </summary>
public class E04ConfirmacionSinPlantillasTests : IDisposable
{
    private readonly ArnesEscenario _arnes = new();

    /// <summary>Lleva la conversacion hasta tener el enlace del formulario enviado.</summary>
    private Task ConEnlaceEnviadoAsync() => _arnes.ConversarAsync(
        new Entrante("Hola"),
        new ConsumirOutbox(),
        new Despachar(),
        new Boton("cuenta_7", "Alicorp"),
        new ConsumirOutbox(),
        new Despachar());

    [Fact]
    public async Task Completar_el_formulario_dentro_de_la_ventana_confirma_en_texto()
    {
        await ConEnlaceEnviadoAsync();

        await _arnes.ConversarAsync(
            new Formulario(),
            new ConsumirOutbox(),
            new Despachar());

        var confirmacion = _arnes.Enviados().Last();

        Assert.Equal("texto", confirmacion.Tipo);
        Assert.Contains("Recibimos tu ficha", confirmacion.Detalle);
        Assert.Contains("Operario de produccion", confirmacion.Detalle);

        // Ninguna plantilla se activo para lograrlo: siguen esperando la aprobacion de Meta.
        Assert.DoesNotContain(_arnes.Enviados(), e => e.Tipo == "plantilla");
        Assert.Empty(await _arnes.Alertas());
    }

    /// <summary>
    /// A las 24h de silencio la ventana ya cerro: el recordatorio necesita plantilla. Sin ella no sale
    /// y no se sella, porque sellarlo lo perderia para siempre (el barrido lo daria por hecho).
    /// </summary>
    [Fact]
    public async Task Sin_plantilla_el_recordatorio_no_sale_deja_alerta_y_no_se_sella()
    {
        await ConEnlaceEnviadoAsync();

        await _arnes.ConversarAsync(
            new Avanzar(TimeSpan.FromHours(25)),
            new Barrido(),
            new ConsumirOutbox(),
            new Despachar());

        Assert.DoesNotContain(_arnes.Enviados(), e => e.Detalle.Contains("aun no completas"));

        var alerta = Assert.Single(await _arnes.Alertas());
        Assert.Equal(TiposAlerta.PlantillaNoAprobada, alerta.Tipo);

        Assert.False((await InvitacionAsync()).RecordatorioEnviado);
    }

    [Fact]
    public async Task Con_la_plantilla_aprobada_el_recordatorio_sale_y_se_sella()
    {
        await ConEnlaceEnviadoAsync();
        await AprobarPlantillaAsync(ClavesPlantilla.RecordatorioJobForms);

        await _arnes.ConversarAsync(
            new Avanzar(TimeSpan.FromHours(25)),
            new Barrido(),
            new ConsumirOutbox(),
            new Despachar());

        Assert.Contains(_arnes.Enviados(), e => e.Tipo == "plantilla" && e.Detalle == ClavesPlantilla.RecordatorioJobForms);
        Assert.True((await InvitacionAsync()).RecordatorioEnviado);
    }

    private Task<JobFormsInvitacion> InvitacionAsync() =>
        _arnes.Entorno.Db.JobFormsInvitaciones.AsNoTracking().OrderByDescending(i => i.InvitacionId).FirstAsync();

    /// <summary>Lo que hace una persona en la administracion cuando Meta aprueba: nunca el codigo.</summary>
    private async Task AprobarPlantillaAsync(string clave)
    {
        var plantilla = await _arnes.Entorno.Db.Plantillas.FirstAsync(p => p.Clave == clave);
        plantilla.Activa = true;

        await _arnes.Entorno.Db.SaveChangesAsync();
    }

    public void Dispose() => _arnes.Dispose();
}
