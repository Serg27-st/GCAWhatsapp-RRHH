using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RRHH.WhatsApp.Api.Controllers;
using RRHH.WhatsApp.Api.Seguridad;
using RRHH.WhatsApp.Contracts.Bandeja;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Tests.Casos;

namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// E23 (FUN-14, M3): el postulante manda su CV por WhatsApp y el analista lo baja desde el chat.
/// <para>
/// Antes quedaba el texto <c>[document]</c> y el archivo se perdía. Ahora se descarga, pasa por el
/// antivirus y aparece en el mensaje; y como es un dato personal, se purga con la retención de la
/// Regla 17.
/// </para>
/// </summary>
public class E23AdjuntoTests : IDisposable
{
    private const string OtroTelefono = "+51911222333";

    private readonly ArnesEscenario _arnes = new();

    private ConversacionesController Controlador() => new(
        _arnes.Entorno.Conversaciones,
        _arnes.Entorno.Mensajes,
        _arnes.Entorno.Postulaciones,
        _arnes.Entorno.Envio,
        _arnes.Entorno.Bandeja,
        _arnes.Entorno.AlmacenamientoAdjuntos,
        _arnes.Entorno.Reloj,
        NullLogger<ConversacionesController>.Instance)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimsAnalista.AnalistaId, EntornoDeReglas.TitularId.ToString()),
                        new Claim(ClaimsAnalista.Rol, "Analista")
                    ],
                    "prueba"))
            }
        }
    };

    /// <summary>El postulante eligió la empresa y el hilo es del titular.</summary>
    private Task AsignadaAsync(string? telefono = null) => _arnes.ConversarAsync(
        new Entrante("Hola", telefono),
        new ConsumirOutbox(),
        new Boton("cuenta_7", "Alicorp", telefono),
        new ConsumirOutbox(),
        new Despachar());

    private async Task<(int ConversacionId, AdjuntoResumen Adjunto)> AdjuntoEnElChatAsync(string? telefono = null)
    {
        var conversacionId = (await _arnes.ConversacionAsync(telefono)).ConversacionId;

        var detalle = Assert.IsType<ConversacionDetalle>(
            Assert.IsType<OkObjectResult>(await Controlador().Detalle(conversacionId, default)).Value);

        var conArchivo = Assert.Single(detalle.Mensajes, m => m.Adjuntos.Count > 0);

        return (conversacionId, Assert.Single(conArchivo.Adjuntos));
    }

    private Task<Domain.Entidades.MensajeAdjunto> AdjuntoEnLaBaseAsync() =>
        _arnes.Entorno.Db.MensajesAdjuntos.AsNoTracking().SingleAsync();

    [Fact]
    public async Task Un_PDF_por_WhatsApp_queda_descargado_y_se_baja_desde_el_chat()
    {
        await AsignadaAsync();

        await _arnes.ConversarAsync(
            new Medio("document", "application/pdf", "CV Maria.pdf"),
            new DescargarAdjuntos());

        var (conversacionId, adjunto) = await AdjuntoEnElChatAsync();

        Assert.Equal(EstadosAdjunto.Descargado, adjunto.Estado);
        Assert.Equal("document", adjunto.Tipo);
        Assert.Equal("CV Maria.pdf", adjunto.NombreArchivo);

        var archivo = Assert.IsType<FileStreamResult>(
            await Controlador().Adjunto(conversacionId, adjunto.AdjuntoId, default));

        Assert.Equal("application/pdf", archivo.ContentType);
        Assert.Equal("CV Maria.pdf", archivo.FileDownloadName);

        await using var contenido = archivo.FileStream;
        using var lector = new StreamReader(contenido, Encoding.ASCII);

        // Lo que entrega el proveedor simulado: el archivo que bajó el Worker, no uno cualquiera.
        Assert.StartsWith("%PDF", await lector.ReadToEndAsync());
    }

    /// <summary>Lo que todavía no pasó el antivirus se muestra, pero no se entrega.</summary>
    [Fact]
    public async Task Mientras_no_se_descarga_se_ve_pero_no_se_entrega()
    {
        await AsignadaAsync();
        await _arnes.ConversarAsync(new Medio("image", "image/jpeg", NombreArchivo: null));

        var (conversacionId, adjunto) = await AdjuntoEnElChatAsync();

        Assert.Equal(EstadosAdjunto.Pendiente, adjunto.Estado);
        Assert.Null(adjunto.NombreArchivo);

        Assert.IsType<NotFoundObjectResult>(
            await Controlador().Adjunto(conversacionId, adjunto.AdjuntoId, default));
    }

    /// <summary>Regla 4: el filtro mira la conversación de la ruta, así que el adjunto tiene que ser de esa.</summary>
    [Fact]
    public async Task El_adjunto_de_otra_conversacion_no_se_entrega()
    {
        await AsignadaAsync();
        await AsignadaAsync(OtroTelefono);

        await _arnes.ConversarAsync(
            new Medio("document", "application/pdf", "cv.pdf", Telefono: OtroTelefono),
            new DescargarAdjuntos());

        var (_, ajeno) = await AdjuntoEnElChatAsync(OtroTelefono);
        var propia = (await _arnes.ConversacionAsync()).ConversacionId;

        Assert.IsType<NotFoundObjectResult>(await Controlador().Adjunto(propia, ajeno.AdjuntoId, default));
    }

    /// <summary>
    /// Regla 17 con el criterio de A5 (V33): pasado el plazo desde la última actividad de la persona, el
    /// archivo se borra y el chat dice que ya no está.
    /// </summary>
    [Fact]
    public async Task Cumplida_la_retencion_el_archivo_se_purga()
    {
        await AsignadaAsync();
        await _arnes.ConversarAsync(new Medio(), new DescargarAdjuntos());

        var guardado = await AdjuntoEnLaBaseAsync();
        var archivoEnDisco = Path.Combine(_arnes.Entorno.CarpetaAdjuntos, guardado.Ruta!);
        Assert.True(File.Exists(archivoEnDisco));

        await _arnes.ConversarAsync(new Avanzar(TimeSpan.FromDays(366)));

        Assert.Equal(1, await _arnes.Entorno.PurgaAdjuntos.ProcesarAsync(maximo: 100));

        var purgado = await AdjuntoEnLaBaseAsync();
        Assert.Equal(EstadoAdjunto.Purgado, purgado.Estado);
        Assert.Null(purgado.Ruta);
        Assert.False(File.Exists(archivoEnDisco));

        var (conversacionId, adjunto) = await AdjuntoEnElChatAsync();
        Assert.Equal(EstadosAdjunto.Purgado, adjunto.Estado);
        Assert.IsType<NotFoundObjectResult>(await Controlador().Adjunto(conversacionId, adjunto.AdjuntoId, default));

        Assert.True(await _arnes.Entorno.Db.Auditorias.AnyAsync(a => a.Accion == "PurgaAdjunto"));
    }

    [Fact]
    public async Task Antes_del_plazo_no_se_purga()
    {
        await AsignadaAsync();
        await _arnes.ConversarAsync(new Medio(), new DescargarAdjuntos(), new Avanzar(TimeSpan.FromDays(300)));

        Assert.Equal(0, await _arnes.Entorno.PurgaAdjuntos.ProcesarAsync(maximo: 100));
        Assert.Equal(EstadoAdjunto.Descargado, (await AdjuntoEnLaBaseAsync()).Estado);
    }

    /// <summary>A5: el CV de alguien que sigue en proceso no se borra, por viejo que sea el mensaje.</summary>
    [Fact]
    public async Task Con_un_proceso_vivo_no_se_purga()
    {
        await AsignadaAsync();

        await _arnes.ConversarAsync(
            new Formulario(),
            new ConsumirOutbox(),
            new Despachar(),
            new Medio(),
            new DescargarAdjuntos(),
            new Avanzar(TimeSpan.FromDays(400)));

        Assert.Equal(EstadoPostulacion.EnProceso, (await _arnes.Entorno.Db.Postulaciones.AsNoTracking().SingleAsync()).Estado);

        Assert.Equal(0, await _arnes.Entorno.PurgaAdjuntos.ProcesarAsync(maximo: 100));
        Assert.Equal(EstadoAdjunto.Descargado, (await AdjuntoEnLaBaseAsync()).Estado);
    }

    public void Dispose() => _arnes.Dispose();
}
