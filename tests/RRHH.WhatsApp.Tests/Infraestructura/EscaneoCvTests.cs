using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Almacenamiento;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// Sección 9.6.1: el CV pasa por el antivirus antes de quedar guardado.
/// <para>
/// El endpoint del JobForms es el único alcanzable desde internet sin autenticación, así que es
/// por donde entraría un archivo hostil.
/// </para>
/// </summary>
public class EscaneoCvTests : IDisposable
{
    /// <summary>Devuelve el veredicto que la prueba necesite, sin depender de Defender.</summary>
    private sealed class EscanerFalso(ResultadoEscaneo resultado) : IEscanerAntivirus
    {
        public List<string> Escaneados { get; } = [];

        public Task<VeredictoEscaneo> EscanearAsync(string rutaArchivo, CancellationToken ct = default)
        {
            Escaneados.Add(rutaArchivo);

            return Task.FromResult(new VeredictoEscaneo(resultado, "de prueba"));
        }
    }

    private readonly string _carpeta =
        Path.Combine(Path.GetTempPath(), $"cv-escaneo-{Guid.NewGuid():N}");

    private AlmacenamientoCvLocal Almacenamiento(IEscanerAntivirus escaner, bool exigir = true) =>
        new(Options.Create(new OpcionesCv
            {
                Carpeta = _carpeta,
                Antivirus = new OpcionesAntivirus { ExigirEscaneo = exigir }
            }),
            escaner,
            NullLogger<AlmacenamientoCvLocal>.Instance);

    private static MemoryStream Cv() => new(Encoding.UTF8.GetBytes("%PDF-1.4 cv"));

    private string[] ArchivosGuardados() =>
        Directory.Exists(_carpeta)
            ? [.. Directory.GetFiles(_carpeta, "*", SearchOption.AllDirectories)]
            : [];

    [Fact]
    public async Task Un_CV_limpio_se_guarda()
    {
        var escaner = new EscanerFalso(ResultadoEscaneo.Limpio);

        var ruta = await Almacenamiento(escaner).GuardarAsync(Cv(), "cv.pdf", "application/pdf");

        Assert.True(File.Exists(Path.Combine(_carpeta, ruta)));
        Assert.Single(escaner.Escaneados);
    }

    [Fact]
    public async Task El_escaneo_ocurre_antes_de_dejar_el_archivo_en_su_lugar()
    {
        // Escribir en el destino y escanear despues dejaria una ventana con un archivo sin
        // revisar al alcance de los analistas.
        var escaner = new EscanerFalso(ResultadoEscaneo.Limpio);

        var ruta = await Almacenamiento(escaner).GuardarAsync(Cv(), "cv.pdf", "application/pdf");

        var escaneado = Assert.Single(escaner.Escaneados);

        Assert.Contains(AlmacenamientoCvLocal.CarpetaCuarentena, escaneado);
        Assert.NotEqual(Path.Combine(_carpeta, ruta), escaneado);
    }

    [Fact]
    public async Task Un_CV_con_amenaza_se_rechaza_y_no_queda_nada()
    {
        var almacenamiento = Almacenamiento(new EscanerFalso(ResultadoEscaneo.Amenaza));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => almacenamiento.GuardarAsync(Cv(), "cv.pdf", "application/pdf"));

        Assert.Contains("antivirus", ex.Message);
        Assert.Empty(ArchivosGuardados());
    }

    [Fact]
    public async Task Si_el_antivirus_no_responde_se_rechaza_por_defecto()
    {
        // Fail-closed: guardar sin escanear porque el escaner estaba caido es justo el caso que
        // el requisito quiere evitar, y un CV rechazado se puede volver a subir.
        var almacenamiento = Almacenamiento(new EscanerFalso(ResultadoEscaneo.NoDisponible));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => almacenamiento.GuardarAsync(Cv(), "cv.pdf", "application/pdf"));

        Assert.Empty(ArchivosGuardados());
    }

    [Fact]
    public async Task Con_ExigirEscaneo_en_falso_un_antivirus_caido_deja_pasar_el_CV()
    {
        // Es una decision explicita de quien configura, no el comportamiento por defecto.
        var almacenamiento = Almacenamiento(new EscanerFalso(ResultadoEscaneo.NoDisponible), exigir: false);

        var ruta = await almacenamiento.GuardarAsync(Cv(), "cv.pdf", "application/pdf");

        Assert.True(File.Exists(Path.Combine(_carpeta, ruta)));
    }

    [Fact]
    public async Task Un_rechazo_no_deja_restos_en_cuarentena()
    {
        // Un archivo huerfano en el recurso compartido no tiene fila que lo referencie: la purga
        // de la Regla 17 nunca lo encontraria.
        var almacenamiento = Almacenamiento(new EscanerFalso(ResultadoEscaneo.Amenaza));

        for (var i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => almacenamiento.GuardarAsync(Cv(), "cv.pdf", "application/pdf"));
        }

        Assert.Empty(ArchivosGuardados());
    }

    [Fact]
    public async Task Una_extension_prohibida_se_corta_antes_de_escanear()
    {
        // No tiene sentido gastar un escaneo en algo que no se iba a aceptar igual.
        var escaner = new EscanerFalso(ResultadoEscaneo.Limpio);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Almacenamiento(escaner).GuardarAsync(Cv(), "cv.exe", "application/octet-stream"));

        Assert.Empty(escaner.Escaneados);
    }

    public void Dispose()
    {
        if (Directory.Exists(_carpeta))
            Directory.Delete(_carpeta, recursive: true);
    }
}
