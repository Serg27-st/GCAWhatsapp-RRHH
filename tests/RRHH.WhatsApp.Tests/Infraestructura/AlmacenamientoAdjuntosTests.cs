using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Domain.Excepciones;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Almacenamiento;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// T5.04 (V33): lo que manda el postulante por WhatsApp pasa por el mismo circuito que el CV del
/// formulario —cuarentena, antivirus, tope y extensiones— antes de quedar al alcance de un analista.
/// </summary>
public class AlmacenamientoAdjuntosTests : IDisposable
{
    private sealed class EscanerFalso(ResultadoEscaneo resultado) : IEscanerAntivirus
    {
        public List<string> Escaneados { get; } = [];

        public Task<VeredictoEscaneo> EscanearAsync(string rutaArchivo, CancellationToken ct = default)
        {
            Escaneados.Add(rutaArchivo);
            return Task.FromResult(new VeredictoEscaneo(resultado, "de prueba"));
        }
    }

    private readonly string _carpetaCv = Path.Combine(Path.GetTempPath(), $"adjuntos-{Guid.NewGuid():N}");

    private string CarpetaAdjuntos => Path.Combine(_carpetaCv, "adjuntos");

    private AlmacenamientoAdjuntosLocal Almacenamiento(
        IEscanerAntivirus escaner, int topeMb = 16, string carpeta = "", bool exigir = true) =>
        new(Options.Create(new OpcionesAdjuntos { Carpeta = carpeta, TamanoMaximoMb = topeMb }),
            Options.Create(new OpcionesCv
            {
                Carpeta = _carpetaCv,
                Antivirus = new OpcionesAntivirus { ExigirEscaneo = exigir }
            }),
            escaner,
            TimeProvider.System,
            NullLogger<AlmacenamientoAdjuntosLocal>.Instance);

    private static MemoryStream Contenido(int bytes = 12) => new(Encoding.ASCII.GetBytes(new string('x', bytes)));

    private string[] ArchivosGuardados() =>
        Directory.Exists(_carpetaCv)
            ? [.. Directory.GetFiles(_carpetaCv, "*", SearchOption.AllDirectories)]
            : [];

    [Fact]
    public async Task Un_documento_limpio_se_guarda_en_la_carpeta_de_adjuntos_con_su_extension()
    {
        var escaner = new EscanerFalso(ResultadoEscaneo.Limpio);

        var guardado = await Almacenamiento(escaner).GuardarAsync(Contenido(12), "Mi CV.PDF", "application/pdf");

        Assert.EndsWith(".pdf", guardado.Ruta);
        Assert.Equal(12, guardado.TamanoBytes);
        Assert.True(File.Exists(Path.Combine(CarpetaAdjuntos, guardado.Ruta)));

        // El nombre que puso la persona nunca es parte de la ruta.
        Assert.DoesNotContain("Mi CV", guardado.Ruta);

        var escaneado = Assert.Single(escaner.Escaneados);
        Assert.Contains(AlmacenamientoArchivosLocal.CarpetaCuarentena, escaneado);
    }

    /// <summary>Solo los documentos traen nombre: una foto o una nota de voz toman la extensión de su tipo.</summary>
    [Theory]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/png", ".png")]
    [InlineData("audio/ogg; codecs=opus", ".ogg")]
    [InlineData("video/mp4", ".mp4")]
    [InlineData("image/webp", ".webp")]
    public async Task Sin_nombre_la_extension_sale_del_tipo(string mime, string extension)
    {
        var guardado = await Almacenamiento(new EscanerFalso(ResultadoEscaneo.Limpio))
            .GuardarAsync(Contenido(), null, mime);

        Assert.EndsWith(extension, guardado.Ruta);
    }

    [Fact]
    public async Task Una_amenaza_se_rechaza_en_firme_y_no_queda_nada()
    {
        var almacenamiento = Almacenamiento(new EscanerFalso(ResultadoEscaneo.Amenaza));

        var ex = await Assert.ThrowsAsync<ArchivoRechazadoException>(
            () => almacenamiento.GuardarAsync(Contenido(), "cv.pdf", "application/pdf"));

        Assert.Contains("antivirus", ex.Message);
        Assert.Empty(ArchivosGuardados());
    }

    [Fact]
    public async Task Un_archivo_que_pasa_el_tope_se_rechaza_en_firme_y_no_queda_nada()
    {
        var almacenamiento = Almacenamiento(new EscanerFalso(ResultadoEscaneo.Limpio), topeMb: 1);

        var ex = await Assert.ThrowsAsync<ArchivoRechazadoException>(
            () => almacenamiento.GuardarAsync(Contenido(1024 * 1024 + 1), "video.mp4", "video/mp4"));

        Assert.Contains("1 MB", ex.Message);
        Assert.Empty(ArchivosGuardados());
    }

    [Theory]
    [InlineData("instalador.exe", "application/octet-stream")]
    [InlineData("cv.pdf.exe", "application/pdf")]
    [InlineData(null, "application/x-msdownload")]
    public async Task Una_extension_no_permitida_se_rechaza_sin_escanear(string? nombre, string mime)
    {
        var escaner = new EscanerFalso(ResultadoEscaneo.Limpio);

        await Assert.ThrowsAsync<ArchivoRechazadoException>(
            () => Almacenamiento(escaner).GuardarAsync(Contenido(), nombre, mime));

        Assert.Empty(escaner.Escaneados);
        Assert.Empty(ArchivosGuardados());
    }

    /// <summary>
    /// Un antivirus caído no es un rechazo: el archivo puede estar bien. No es una
    /// <see cref="ArchivoRechazadoException"/>, y quien descarga lo vuelve a intentar.
    /// </summary>
    [Fact]
    public async Task Si_el_antivirus_no_responde_falla_sin_rechazar_en_firme()
    {
        var almacenamiento = Almacenamiento(new EscanerFalso(ResultadoEscaneo.NoDisponible));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => almacenamiento.GuardarAsync(Contenido(), "cv.pdf", "application/pdf"));

        Assert.IsNotType<ArchivoRechazadoException>(ex);
        Assert.Empty(ArchivosGuardados());
    }

    [Fact]
    public async Task Con_carpeta_propia_no_usa_la_de_los_CVs()
    {
        var propia = Path.Combine(_carpetaCv, "otra");

        var guardado = await Almacenamiento(new EscanerFalso(ResultadoEscaneo.Limpio), carpeta: propia)
            .GuardarAsync(Contenido(), "cv.pdf", "application/pdf");

        Assert.True(File.Exists(Path.Combine(propia, guardado.Ruta)));
    }

    [Fact]
    public async Task Lo_guardado_se_puede_leer_y_borrar()
    {
        var almacenamiento = Almacenamiento(new EscanerFalso(ResultadoEscaneo.Limpio));
        var guardado = await almacenamiento.GuardarAsync(Contenido(5), "cv.pdf", "application/pdf");

        await using (var leido = await almacenamiento.ObtenerAsync(guardado.Ruta))
        {
            Assert.NotNull(leido);
            Assert.Equal(5, leido.Length);
        }

        await almacenamiento.EliminarAsync(guardado.Ruta);

        Assert.Null(await almacenamiento.ObtenerAsync(guardado.Ruta));
    }

    /// <summary>
    /// Una ruta manipulada en la base no puede salir de la carpeta, tampoco hacia una hermana cuyo nombre
    /// empieza igual (<c>adjuntos-viejos</c> junto a <c>adjuntos</c>).
    /// </summary>
    [Theory]
    [InlineData("..\\secreto.txt")]
    [InlineData("..\\adjuntos-viejos\\secreto.txt")]
    public async Task Una_ruta_fuera_de_la_carpeta_no_se_lee_ni_se_borra(string ruta)
    {
        var fuera = Path.GetFullPath(Path.Combine(CarpetaAdjuntos, ruta));
        Directory.CreateDirectory(Path.GetDirectoryName(fuera)!);
        await File.WriteAllTextAsync(fuera, "no tocar");

        var almacenamiento = Almacenamiento(new EscanerFalso(ResultadoEscaneo.Limpio));

        Assert.Null(await almacenamiento.ObtenerAsync(ruta));

        await almacenamiento.EliminarAsync(ruta);

        Assert.True(File.Exists(fuera));
    }

    public void Dispose()
    {
        if (Directory.Exists(_carpetaCv))
            Directory.Delete(_carpetaCv, recursive: true);
    }
}
