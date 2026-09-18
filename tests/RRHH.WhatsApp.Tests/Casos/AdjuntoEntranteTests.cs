using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Tests.Escenarios;
using RRHH.WhatsApp.Tests.Proveedores;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// T5.02 (ARQ-10, M3): lo que el postulante manda como archivo queda registrado para descargarlo.
/// <para>
/// Antes quedaba el texto <c>[document]</c> y nada más. El id de medio de Meta caduca, así que lo que
/// no se registra al recibirlo ya no se puede recuperar después.
/// </para>
/// </summary>
public class AdjuntoEntranteTests : IDisposable
{
    private readonly ArnesEscenario _arnes = new();

    private Task<List<MensajeAdjunto>> AdjuntosAsync() =>
        _arnes.Entorno.Db.MensajesAdjuntos.AsNoTracking().ToListAsync();

    [Fact]
    public async Task Un_documento_queda_como_adjunto_pendiente_con_nombre_y_tipo()
    {
        await _arnes.ConversarAsync(new Medio("document", "application/pdf", "cv.pdf"));

        var mensaje = await _arnes.Entorno.Db.Mensajes.AsNoTracking()
            .Include(m => m.Adjuntos)
            .SingleAsync(m => m.Direccion == DireccionMensaje.Entrante);

        Assert.Equal("[documento: cv.pdf]", mensaje.Contenido);

        var adjunto = Assert.Single(mensaje.Adjuntos);
        Assert.Equal(EstadoAdjunto.Pendiente, adjunto.Estado);
        Assert.Equal("document", adjunto.TipoMedio);
        Assert.Equal("application/pdf", adjunto.MimeType);
        Assert.Equal("cv.pdf", adjunto.NombreArchivo);
        Assert.StartsWith("medio.escenario.", adjunto.ProveedorMedioId);
        Assert.Equal(_arnes.Entorno.Ahora, adjunto.FechaRecepcion);

        // Todavía no se bajó: ni ruta ni tamaño hasta que el Worker lo descargue y lo escanee.
        Assert.Null(adjunto.Ruta);
        Assert.Null(adjunto.TamanoBytes);
    }

    /// <summary>P4: Meta reentrega el webhook; el mensaje se descarta y su adjunto también.</summary>
    [Fact]
    public async Task La_reentrega_del_webhook_no_duplica_el_adjunto()
    {
        var sinFirma = new Dictionary<string, string>();

        await _arnes.Entorno.Recepcion.ProcesarAsync(PayloadsDePrueba.Adjunto, sinFirma);
        await _arnes.Entorno.Recepcion.ProcesarAsync(PayloadsDePrueba.Adjunto, sinFirma);

        var adjunto = Assert.Single(await AdjuntosAsync());
        Assert.Equal("1037543291543636", adjunto.ProveedorMedioId);
    }

    [Fact]
    public async Task Un_texto_no_registra_adjuntos()
    {
        await _arnes.ConversarAsync(new Entrante("Hola"));

        Assert.Empty(await AdjuntosAsync());
    }

    /// <summary>
    /// La leyenda es texto de la persona: el código del aviso escrito junto a la foto del DNI tiene que
    /// llevarla al formulario igual que si lo hubiera escrito solo (FUN-02).
    /// </summary>
    [Fact]
    public async Task La_leyenda_llega_a_las_reglas_como_el_texto_del_mensaje()
    {
        var vacante = await _arnes.Entorno.Db.Hcs.FirstAsync();
        vacante.CodigoAviso = "K7M2QX";
        await _arnes.Entorno.Db.SaveChangesAsync();

        await _arnes.ConversarAsync(
            new Medio("image", "image/jpeg", NombreArchivo: null, Leyenda: "Hola, postulo a K7M2QX"),
            new ConsumirOutbox(),
            new Despachar());

        Assert.Contains(_arnes.Enviados(), e => e.Detalle.Contains("completa esta ficha"));
        Assert.Single(await AdjuntosAsync());
    }

    public void Dispose() => _arnes.Dispose();
}
