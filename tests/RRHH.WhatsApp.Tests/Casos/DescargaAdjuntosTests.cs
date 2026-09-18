using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Almacenamiento;
using RRHH.WhatsApp.Infrastructure.Servicios;
using RRHH.WhatsApp.Tests.Escenarios;
using RRHH.WhatsApp.Tests.Proveedores;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// T5.04 (V33): el Worker baja lo que el webhook registró, lo escanea y lo guarda. Lo que no pasa el
/// escaneo queda rechazado; lo que falló por el proveedor se reintenta con espera, pero no para
/// siempre, porque el id de medio caduca.
/// </summary>
public class DescargaAdjuntosTests : IDisposable
{
    private sealed class EscanerFalso(ResultadoEscaneo resultado) : IEscanerAntivirus
    {
        public Task<VeredictoEscaneo> EscanearAsync(string rutaArchivo, CancellationToken ct = default) =>
            Task.FromResult(new VeredictoEscaneo(resultado, "de prueba"));
    }

    private static readonly ParametrosDescargaAdjuntos Parametros = new(
        TamanoLote: 10,
        IntentosMaximos: 3,
        EsperaBase: TimeSpan.FromMinutes(1),
        TiempoMaximoPorArchivo: TimeSpan.FromMinutes(5));

    private readonly ArnesEscenario _arnes = new();
    private readonly ProveedorFalso _proveedor = new();
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), $"descarga-{Guid.NewGuid():N}");

    private DescargaAdjuntos Descarga(
        IWhatsAppProvider? proveedor = null, ResultadoEscaneo escaneo = ResultadoEscaneo.Limpio, int topeMb = 16)
    {
        var db = _arnes.Entorno.Db;
        var reloj = _arnes.Entorno.Reloj;

        return new DescargaAdjuntos(
            new MensajeService(db, reloj, NullLogger<MensajeService>.Instance),
            proveedor ?? _proveedor,
            new AlmacenamientoAdjuntosLocal(
                Options.Create(new OpcionesAdjuntos { Carpeta = _carpeta, TamanoMaximoMb = topeMb }),
                Options.Create(new OpcionesCv { Carpeta = _carpeta }),
                new EscanerFalso(escaneo),
                reloj,
                NullLogger<AlmacenamientoAdjuntosLocal>.Instance),
            reloj,
            NullLogger<DescargaAdjuntos>.Instance);
    }

    private static Task<ResultadoDescarga> Archivo(int bytes = 20) =>
        Task.FromResult(ResultadoDescarga.Ok(
            new MedioDescargado(new MemoryStream(new byte[bytes]), "application/pdf", bytes)));

    private Task RecibirDocumentoAsync() =>
        _arnes.ConversarAsync(new Medio("document", "application/pdf", "cv.pdf"));

    private Task<MensajeAdjunto> AdjuntoAsync() =>
        _arnes.Entorno.Db.MensajesAdjuntos.AsNoTracking().SingleAsync();

    private string[] ArchivosGuardados() =>
        Directory.Exists(_carpeta)
            ? [.. Directory.GetFiles(_carpeta, "*", SearchOption.AllDirectories)]
            : [];

    [Fact]
    public async Task Un_archivo_limpio_queda_descargado_con_su_ruta_y_su_tamano()
    {
        await RecibirDocumentoAsync();
        _proveedor.Descarga = (_, _) => Archivo(20);

        var resumen = await Descarga().ProcesarAsync(Parametros);

        Assert.Equal(1, resumen.Descargados);

        var adjunto = await AdjuntoAsync();
        Assert.Equal(EstadoAdjunto.Descargado, adjunto.Estado);
        Assert.Equal(20, adjunto.TamanoBytes);
        Assert.Equal(1, adjunto.IntentosDescarga);
        Assert.Null(adjunto.Error);
        Assert.Null(adjunto.ProximoIntentoUtc);
        Assert.True(File.Exists(Path.Combine(_carpeta, adjunto.Ruta!)));

        Assert.Equal([adjunto.ProveedorMedioId], _proveedor.Descargas);
    }

    [Fact]
    public async Task Lo_descargado_no_se_vuelve_a_pedir()
    {
        await RecibirDocumentoAsync();
        _proveedor.Descarga = (_, _) => Archivo();

        await Descarga().ProcesarAsync(Parametros);
        var segunda = await Descarga().ProcesarAsync(Parametros);

        Assert.Equal(0, segunda.Intentados);
        Assert.Single(_proveedor.Descargas);
    }

    [Fact]
    public async Task Una_amenaza_queda_rechazada_y_no_se_guarda()
    {
        await RecibirDocumentoAsync();
        _proveedor.Descarga = (_, _) => Archivo();

        var resumen = await Descarga(escaneo: ResultadoEscaneo.Amenaza).ProcesarAsync(Parametros);

        Assert.Equal(1, resumen.Rechazados);

        var adjunto = await AdjuntoAsync();
        Assert.Equal(EstadoAdjunto.Rechazado, adjunto.Estado);
        Assert.Contains("antivirus", adjunto.Error);
        Assert.Null(adjunto.Ruta);
        Assert.Null(adjunto.ProximoIntentoUtc);
        Assert.Empty(ArchivosGuardados());
    }

    [Fact]
    public async Task Un_archivo_que_pasa_el_tope_queda_rechazado()
    {
        await RecibirDocumentoAsync();
        _proveedor.Descarga = (_, _) => Archivo(1024 * 1024 + 1);

        await Descarga(topeMb: 1).ProcesarAsync(Parametros);

        var adjunto = await AdjuntoAsync();
        Assert.Equal(EstadoAdjunto.Rechazado, adjunto.Estado);
        Assert.Contains("1 MB", adjunto.Error);
        Assert.Empty(ArchivosGuardados());
    }

    /// <summary>Un id vencido no aparece reintentando: se da por perdido en el primer intento.</summary>
    [Fact]
    public async Task Un_rechazo_en_firme_del_proveedor_no_se_reintenta()
    {
        await RecibirDocumentoAsync();
        _proveedor.Descarga = (_, _) => Task.FromResult(ResultadoDescarga.Permanente("HTTP 404 al pedir los datos del medio"));

        await Descarga().ProcesarAsync(Parametros);

        var adjunto = await AdjuntoAsync();
        Assert.Equal(EstadoAdjunto.Rechazado, adjunto.Estado);
        Assert.Equal(1, adjunto.IntentosDescarga);
        Assert.Null(adjunto.ProximoIntentoUtc);
        Assert.Contains("404", adjunto.Error);
    }

    [Fact]
    public async Task Un_fallo_transitorio_se_reintenta_despues_de_esperar_y_cada_vez_espera_mas()
    {
        await RecibirDocumentoAsync();
        _proveedor.Descarga = (_, _) => Task.FromResult(ResultadoDescarga.Transitorio("HTTP 503"));

        var resumen = await Descarga().ProcesarAsync(Parametros);

        Assert.Equal(1, resumen.Reprogramados);

        var adjunto = await AdjuntoAsync();
        Assert.Equal(EstadoAdjunto.Pendiente, adjunto.Estado);
        Assert.Equal(1, adjunto.IntentosDescarga);
        Assert.Equal(_arnes.Entorno.Ahora.AddMinutes(1), adjunto.ProximoIntentoUtc);
        Assert.Contains("503", adjunto.Error);

        // Antes de la hora no se vuelve a pedir.
        await Descarga().ProcesarAsync(Parametros);
        Assert.Single(_proveedor.Descargas);

        await _arnes.ConversarAsync(new Avanzar(TimeSpan.FromMinutes(1)));
        await Descarga().ProcesarAsync(Parametros);

        Assert.Equal(2, _proveedor.Descargas.Count);
        Assert.Equal(_arnes.Entorno.Ahora.AddMinutes(2), (await AdjuntoAsync()).ProximoIntentoUtc);
    }

    [Fact]
    public async Task Agotados_los_intentos_queda_rechazado()
    {
        await RecibirDocumentoAsync();
        _proveedor.Descarga = (_, _) => Task.FromResult(ResultadoDescarga.Transitorio("HTTP 503"));

        for (var i = 0; i < Parametros.IntentosMaximos; i++)
        {
            await Descarga().ProcesarAsync(Parametros);
            await _arnes.ConversarAsync(new Avanzar(TimeSpan.FromHours(1)));
        }

        var adjunto = await AdjuntoAsync();
        Assert.Equal(EstadoAdjunto.Rechazado, adjunto.Estado);
        Assert.Equal(Parametros.IntentosMaximos, adjunto.IntentosDescarga);
        Assert.Null(adjunto.ProximoIntentoUtc);

        await Descarga().ProcesarAsync(Parametros);
        Assert.Equal(Parametros.IntentosMaximos, _proveedor.Descargas.Count);
    }

    /// <summary>Un antivirus caído no dice nada del archivo: se vuelve a intentar, sin guardarlo sin escanear.</summary>
    [Fact]
    public async Task Si_el_antivirus_no_responde_se_reintenta_sin_guardar()
    {
        await RecibirDocumentoAsync();
        _proveedor.Descarga = (_, _) => Archivo();

        await Descarga(escaneo: ResultadoEscaneo.NoDisponible).ProcesarAsync(Parametros);

        var adjunto = await AdjuntoAsync();
        Assert.Equal(EstadoAdjunto.Pendiente, adjunto.Estado);
        Assert.NotNull(adjunto.ProximoIntentoUtc);
        Assert.Empty(ArchivosGuardados());
    }

    /// <summary>Una excepción inesperada con un adjunto no frena a los demás del lote.</summary>
    [Fact]
    public async Task Un_adjunto_que_falla_no_frena_al_resto_del_lote()
    {
        await RecibirDocumentoAsync();
        await RecibirDocumentoAsync();

        var llamadas = 0;
        _proveedor.Descarga = (_, _) => ++llamadas == 1
            ? throw new IOException("se cayo algo que nadie esperaba")
            : Archivo();

        var resumen = await Descarga().ProcesarAsync(Parametros);

        Assert.Equal(2, resumen.Intentados);
        Assert.Equal(1, resumen.Descargados);
        Assert.Equal(1, resumen.Reprogramados);
    }

    /// <summary>
    /// Una descarga que no termina no puede frenar el bucle: pasado el tiempo máximo se corta y se
    /// reintenta más tarde (se mide con el reloj inyectado, ARQ-01).
    /// </summary>
    [Fact]
    public async Task Una_descarga_colgada_se_corta_al_tiempo_maximo()
    {
        await RecibirDocumentoAsync();
        _proveedor.Descarga = async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return ResultadoDescarga.Permanente("inalcanzable");
        };

        var tarea = Descarga().ProcesarAsync(Parametros);

        while (_proveedor.Descargas.Count == 0)
            await Task.Delay(10).WaitAsync(TimeSpan.FromSeconds(10));

        _arnes.Entorno.Reloj.Advance(Parametros.TiempoMaximoPorArchivo);

        var resumen = await tarea.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, resumen.Reprogramados);
        Assert.Equal(EstadoAdjunto.Pendiente, (await AdjuntoAsync()).Estado);
    }

    /// <summary>En desarrollo el circuito completo corre con el proveedor simulado.</summary>
    [Fact]
    public async Task Con_el_proveedor_simulado_el_circuito_termina_descargado()
    {
        await RecibirDocumentoAsync();

        await Descarga(_arnes.Entorno.Proveedor).ProcesarAsync(Parametros);

        var adjunto = await AdjuntoAsync();
        Assert.Equal(EstadoAdjunto.Descargado, adjunto.Estado);
        Assert.Contains(adjunto.ProveedorMedioId, _arnes.Entorno.Proveedor.MediosDescargados);
    }

    public void Dispose()
    {
        _arnes.Dispose();

        if (Directory.Exists(_carpeta))
            Directory.Delete(_carpeta, recursive: true);
    }
}
