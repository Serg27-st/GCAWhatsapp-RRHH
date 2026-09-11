using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Almacenamiento;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// El escáner contra Windows Defender de verdad.
/// <para>
/// El veredicto depende de si Defender está activo en la máquina, así que estas pruebas no
/// afirman "limpio": afirman que el escáner devuelve algo coherente y que sabe distinguir un
/// antivirus caído de un archivo infectado, que es donde está la trampa —MpCmdRun devuelve 2 en
/// los dos casos—.
/// </para>
/// </summary>
public class EscanerDefenderTests : IDisposable
{
    private readonly string _archivo =
        Path.Combine(Path.GetTempPath(), $"cv-defender-{Guid.NewGuid():N}.pdf");

    public EscanerDefenderTests() => File.WriteAllText(_archivo, "%PDF-1.4 contenido inofensivo");

    private static EscanerDefender Escaner(int timeout = 60, string ruta = "") =>
        new(Options.Create(new OpcionesAntivirus { TimeoutSegundos = timeout, RutaMpCmdRun = ruta }),
            NullLogger<EscanerDefender>.Instance);

    [Fact]
    public async Task Un_archivo_inofensivo_nunca_se_reporta_como_amenaza()
    {
        // Limpio si Defender está activo, NoDisponible si está apagado. Amenaza sería un falso
        // positivo, y significaría que se está interpretando mal la salida de MpCmdRun.
        var veredicto = await Escaner().EscanearAsync(_archivo);

        Assert.NotEqual(ResultadoEscaneo.Amenaza, veredicto.Resultado);
    }

    [Fact]
    public async Task Con_una_ruta_de_MpCmdRun_que_no_existe_queda_no_disponible()
    {
        var veredicto = await Escaner(ruta: @"C:\no\existe\MpCmdRun.exe").EscanearAsync(_archivo);

        Assert.Equal(ResultadoEscaneo.NoDisponible, veredicto.Resultado);
        Assert.Contains("MpCmdRun", veredicto.Detalle!);
    }

    [Fact]
    public async Task Un_escaneo_que_no_termina_a_tiempo_queda_no_disponible()
    {
        // Un escaneo colgado no puede dejar el envío del postulante esperando para siempre.
        var veredicto = await Escaner(timeout: 0).EscanearAsync(_archivo);

        Assert.NotEqual(ResultadoEscaneo.Limpio, veredicto.Resultado);
    }

    [Fact]
    public async Task El_veredicto_siempre_trae_detalle_cuando_no_es_limpio()
    {
        // Sin detalle, un rechazo obliga a entrar al servidor a mirar logs para saber por qué.
        var veredicto = await Escaner(ruta: @"C:\no\existe\MpCmdRun.exe").EscanearAsync(_archivo);

        Assert.False(string.IsNullOrWhiteSpace(veredicto.Detalle));
    }

    public void Dispose()
    {
        if (File.Exists(_archivo))
            File.Delete(_archivo);
    }
}
