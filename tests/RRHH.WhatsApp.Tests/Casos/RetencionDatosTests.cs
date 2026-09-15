using System.Text;
using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Tests.Proveedores;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// Regla 17 — consentimiento y proteccion de datos: donde viven los CVs, cuanto duran y que pasa
/// cuando el postulante pide que se eliminen sus datos.
/// </summary>
public class RetencionDatosTests : IDisposable
{
    private readonly EntornoDeReglas _entorno = new();

    private async Task<Guid> ConEnlaceEnviadoAsync()
    {
        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);
        await _entorno.ConsumirOutboxAsync();

        return (await _entorno.Db.JobFormsInvitaciones.AsNoTracking().FirstAsync()).Token;
    }

    private async Task<string> GuardarCvAsync()
    {
        var contenido = new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.4 cv de prueba"));

        return await _entorno.Formularios.AlmacenarCvAsync(contenido, "cv.pdf", "application/pdf");
    }

    /// <summary>Deja una respuesta con CV propio, ya vencida segun el plazo indicado.</summary>
    private async Task<int> ConCvVencidoAsync(string ruta, int diasDeAntiguedad)
    {
        var token = await ConEnlaceEnviadoAsync();

        var envio = new EnvioJobForms(
            token,
            new DatosPostulanteFormulario("45678912", "Maria Quispe", "+51987654321", null),
            "{}",
            ruta,
            ConsentimientoAceptado: true);

        var resultado = await _entorno.RecepcionFormulario.ProcesarAsync(envio);

        var respuesta = await _entorno.Db.JobFormsRespuestas
            .FirstAsync(r => r.RespuestaId == resultado.RespuestaId);

        respuesta.FechaEnvio = _entorno.Ahora.AddDays(-diasDeAntiguedad);

        await _entorno.Db.SaveChangesAsync();

        return resultado.RespuestaId;
    }

    [Fact]
    public async Task El_CV_se_guarda_fuera_de_la_base_y_se_puede_recuperar()
    {
        // Seccion 8.3: los adjuntos no van dentro de SQL Server.
        var ruta = await GuardarCvAsync();

        Assert.False(Path.IsPathRooted(ruta));
        Assert.True(File.Exists(Path.Combine(_entorno.CarpetaCv, ruta)));
    }

    [Fact]
    public async Task No_se_admite_un_adjunto_con_extension_fuera_de_la_lista()
    {
        var contenido = new MemoryStream(Encoding.UTF8.GetBytes("MZ"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _entorno.Formularios.AlmacenarCvAsync(contenido, "cv.exe", "application/octet-stream"));
    }

    [Fact]
    public async Task Cumplido_el_plazo_el_CV_se_borra_y_su_referencia_queda_limpia()
    {
        var ruta = await GuardarCvAsync();
        var respuestaId = await ConCvVencidoAsync(ruta, diasDeAntiguedad: 400);

        var vencidas = await _entorno.Formularios.ListarCvsPorPurgarAsync(diasRetencion: 365, maximo: 50);
        Assert.Contains(vencidas, r => r.RespuestaId == respuestaId);

        await _entorno.Formularios.PurgarCvAsync(respuestaId);

        Assert.False(File.Exists(Path.Combine(_entorno.CarpetaCv, ruta)));

        var respuesta = await _entorno.Db.JobFormsRespuestas.AsNoTracking()
            .FirstAsync(r => r.RespuestaId == respuestaId);

        Assert.Null(respuesta.CvUrl);
    }

    [Fact]
    public async Task Antes_del_plazo_el_CV_no_entra_en_la_purga()
    {
        var ruta = await GuardarCvAsync();
        var respuestaId = await ConCvVencidoAsync(ruta, diasDeAntiguedad: 30);

        var vencidas = await _entorno.Formularios.ListarCvsPorPurgarAsync(diasRetencion: 365, maximo: 50);

        Assert.DoesNotContain(vencidas, r => r.RespuestaId == respuestaId);
    }

    [Fact]
    public async Task La_purga_deja_constancia_en_la_auditoria()
    {
        var ruta = await GuardarCvAsync();
        var respuestaId = await ConCvVencidoAsync(ruta, diasDeAntiguedad: 400);

        await _entorno.Formularios.PurgarCvAsync(respuestaId);

        var registrada = await _entorno.Db.Auditorias.AnyAsync(a => a.Accion == "PurgaCv");
        Assert.True(registrada);
    }

    [Fact]
    public async Task La_eliminacion_solicitada_anonimiza_sin_borrar_el_historial()
    {
        // Borrar al postulante se llevaria sus postulaciones, y con ellas las metricas de la
        // Regla 18. Se vacian los datos personales y se conserva la fila.
        var ruta = await GuardarCvAsync();
        await ConCvVencidoAsync(ruta, diasDeAntiguedad: 1);

        await _entorno.Postulantes.AnonimizarDatosAsync("45678912", "Solicitud del titular.");

        var postulante = await _entorno.Db.Postulantes.AsNoTracking().FirstAsync();

        Assert.StartsWith("ANON-", postulante.Dni);
        Assert.Null(postulante.NombreCompleto);
        Assert.Null(postulante.TelefonoUltimo);

        Assert.Single(_entorno.Db.Postulaciones);

        var respuesta = await _entorno.Db.JobFormsRespuestas.AsNoTracking().FirstAsync();
        Assert.Null(respuesta.CvUrl);
        Assert.Equal("{}", respuesta.DatosJson);

        Assert.False(File.Exists(Path.Combine(_entorno.CarpetaCv, ruta)));
    }

    [Fact]
    public async Task Un_CV_que_vive_en_Drive_limpia_la_referencia_aunque_no_pueda_borrar_el_archivo()
    {
        // Mientras el formulario siga en Google Forms, el archivo es del formulario y hay que
        // borrarlo alla. El dato personal si sale de nuestra base.
        var respuestaId = await ConCvVencidoAsync("https://drive.google.com/file/abc", 400);

        await _entorno.Formularios.PurgarCvAsync(respuestaId);

        var respuesta = await _entorno.Db.JobFormsRespuestas.AsNoTracking()
            .FirstAsync(r => r.RespuestaId == respuestaId);

        Assert.Null(respuesta.CvUrl);
    }

    [Fact]
    public async Task No_se_admite_un_CV_que_pase_del_tamano_maximo()
    {
        // Seccion 9.6.1: el endpoint publico es el unico alcanzable desde internet sin login.
        // El tope vive en OpcionesCv (10 MB por defecto en el entorno de prueba).
        var grande = new MemoryStream(new byte[11 * 1024 * 1024]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _entorno.Formularios.AlmacenarCvAsync(grande, "cv.pdf", "application/pdf"));

        Assert.Contains("MB", ex.Message);
    }

    [Fact]
    public async Task Un_CV_rechazado_por_tamano_no_deja_el_archivo_a_medias()
    {
        // Un archivo huerfano en el recurso compartido no tiene fila que lo referencie: la purga
        // de la Regla 17 nunca lo encontraria y nadie sabria de donde salio.
        var antes = ArchivosGuardados();

        var grande = new MemoryStream(new byte[11 * 1024 * 1024]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _entorno.Formularios.AlmacenarCvAsync(grande, "cv.pdf", "application/pdf"));

        Assert.Equal(antes, ArchivosGuardados());
    }

    [Fact]
    public async Task Justo_por_debajo_del_tope_si_se_acepta()
    {
        var contenido = new MemoryStream(new byte[9 * 1024 * 1024]);

        var ruta = await _entorno.Formularios.AlmacenarCvAsync(contenido, "cv.pdf", "application/pdf");

        Assert.True(File.Exists(Path.Combine(_entorno.CarpetaCv, ruta)));
    }

    /// <summary>Cuenta lo que hay en la carpeta, para detectar restos de un envio rechazado.</summary>
    private int ArchivosGuardados() =>
        Directory.Exists(_entorno.CarpetaCv)
            ? Directory.GetFiles(_entorno.CarpetaCv, "*", SearchOption.AllDirectories).Length
            : 0;

    public void Dispose() => _entorno.Dispose();
}
