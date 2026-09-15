using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RRHH.WhatsApp.Api.Configuracion;
using RRHH.WhatsApp.Api.Controllers;
using RRHH.WhatsApp.Api.Seguridad;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Almacenamiento;
using RRHH.WhatsApp.Tests.Proveedores;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// COR-15/M8: lo que entra por <see cref="JobFormsController"/> viene de internet, asi que un DNI
/// nulo, un CvUrl armado a mano o una vacante que se cerro a mitad de camino no pueden terminar en
/// un 500 ni en un CV sin fila que lo referencie (invariante 9 de <c>02-auditoria-codigo.md</c>).
/// </summary>
public class JobFormsControllerTests : IDisposable
{
    private const string Secreto = "secreto-de-prueba";

    private readonly EntornoDeReglas _entorno = new();

    private async Task<Guid> ConEnlaceEnviadoAsync()
    {
        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);
        await _entorno.ConsumirOutboxAsync();

        var invitacion = await _entorno.Db.JobFormsInvitaciones.AsNoTracking().FirstAsync();

        return invitacion.Token;
    }

    private JobFormsController Controlador(IJobFormsService? formularios = null) =>
        new(
            _entorno.RecepcionFormulario,
            _entorno.Invitaciones,
            formularios ?? _entorno.Formularios,
            _entorno.Cuentas,
            Options.Create(new OpcionesJobForms { SecretoWebhook = Secreto }),
            Options.Create(new OpcionesCv { TamanoMaximoMb = 10 }),
            NullLogger<JobFormsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = ConHeaderSecreto(new DefaultHttpContext())
            }
        };

    private static DefaultHttpContext ConHeaderSecreto(DefaultHttpContext contexto)
    {
        contexto.Request.Headers[SecretoJobForms.Cabecera] = Secreto;

        return contexto;
    }

    private static FormFile Cv(string contenido = "%PDF-1.4 cv")
    {
        var bytes = Encoding.UTF8.GetBytes(contenido);

        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "cv", "cv.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
    }

    /// <summary>Todo lo que quedo en la carpeta de CVs, fuera de la cuarentena (que se limpia sola).</summary>
    private IReadOnlyList<string> ArchivosGuardados() =>
        Directory.Exists(_entorno.CarpetaCv)
            ? [.. Directory.GetFiles(_entorno.CarpetaCv, "*", SearchOption.AllDirectories)
                .Where(f => !f.Contains(AlmacenamientoCvLocal.CarpetaCuarentena))]
            : [];

    // ---- WebhookGoogle ------------------------------------------------------------------------

    [Fact]
    public async Task WebhookGoogle_con_DNI_nulo_da_422_y_no_500()
    {
        var token = await ConEnlaceEnviadoAsync();

        // Antes de COR-15/M8, cuerpo.Dni.Trim() sobre un nulo tumbaba el webhook con un 500: es
        // exactamente lo que este envio simula (el JSON puede traer null aunque el record diga
        // string no anulable, porque System.Text.Json no impone eso en tiempo de ejecucion).
        var cuerpo = new JobFormsController.EnvioGoogleForms(
            token.ToString(), null!, "Maria Quispe", "+51987654321", "maria@correo.pe",
            "{}", null, ConsentimientoAceptado: true);

        var resultado = await Controlador().WebhookGoogle(cuerpo, CancellationToken.None);

        var noProcesable = Assert.IsType<UnprocessableEntityObjectResult>(resultado);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, noProcesable.StatusCode);
    }

    [Fact]
    public async Task WebhookGoogle_con_DNI_de_7_digitos_da_422()
    {
        var token = await ConEnlaceEnviadoAsync();

        var cuerpo = new JobFormsController.EnvioGoogleForms(
            token.ToString(), "4567891", null, null, null, "{}", null, ConsentimientoAceptado: true);

        var resultado = await Controlador().WebhookGoogle(cuerpo, CancellationToken.None);

        Assert.IsType<UnprocessableEntityObjectResult>(resultado);
    }

    [Theory]
    [InlineData("https://drive.google.com.evil.com/x")]
    [InlineData("https://evil.com/drive.google.com")]
    [InlineData("http://drive.google.com/file/d/abc/view")]
    [InlineData("javascript:alert(1)")]
    public async Task WebhookGoogle_con_un_CvUrl_malicioso_da_422(string cvUrl)
    {
        var token = await ConEnlaceEnviadoAsync();

        var cuerpo = new JobFormsController.EnvioGoogleForms(
            token.ToString(), "45678912", "Maria Quispe", null, null, "{}", cvUrl,
            ConsentimientoAceptado: true);

        var resultado = await Controlador().WebhookGoogle(cuerpo, CancellationToken.None);

        var noProcesable = Assert.IsType<UnprocessableEntityObjectResult>(resultado);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, noProcesable.StatusCode);

        // El CvUrl viaja como referencia (Google Drive), no como archivo: un rechazo no debe
        // guardar nada, tampoco del lado del formulario propio.
        Assert.Empty(await _entorno.Db.JobFormsRespuestas.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task WebhookGoogle_con_DNI_y_CvUrl_validos_se_procesa()
    {
        var token = await ConEnlaceEnviadoAsync();

        var cuerpo = new JobFormsController.EnvioGoogleForms(
            token.ToString(), "45 678 912", "Maria Quispe", "+51987654321", "maria@correo.pe",
            "{}", "https://drive.google.com/file/d/abc123/view", ConsentimientoAceptado: true);

        var resultado = await Controlador().WebhookGoogle(cuerpo, CancellationToken.None);

        Assert.IsType<OkObjectResult>(resultado);

        // El DNI queda normalizado (sin espacios): es la misma forma en la que despues se busca
        // por PostulantesController y ConversacionesController.Buscar.
        var postulante = await _entorno.Db.Postulantes.AsNoTracking().SingleAsync();
        Assert.Equal("45678912", postulante.Dni);
    }

    // ---- Enviar (camino propio, D3) ------------------------------------------------------------

    private static JobFormsController.EnvioPropio Formulario(string dni = "45678912", bool consentimiento = true) =>
        new(dni, "Maria Quispe", "+51987654321", "maria@correo.pe", "{}", consentimiento);

    [Fact]
    public async Task Enviar_con_DNI_invalido_da_422_y_no_guarda_el_CV()
    {
        var token = await ConEnlaceEnviadoAsync();
        var formularios = Substitute.For<IJobFormsService>();

        var resultado = await Controlador(formularios)
            .Enviar(token, Formulario(dni: "no-es-un-dni"), Cv(), CancellationToken.None);

        Assert.IsType<UnprocessableEntityObjectResult>(resultado);

        await formularios.DidNotReceive().AlmacenarCvAsync(
            Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Enviar_con_vacante_cerrada_da_422_y_no_guarda_el_CV()
    {
        var token = await ConEnlaceEnviadoAsync();

        var formularios = Substitute.For<IJobFormsService>();
        formularios.ValidarVacanteActivaAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var resultado = await Controlador(formularios)
            .Enviar(token, Formulario(), Cv(), CancellationToken.None);

        var noProcesable = Assert.IsType<UnprocessableEntityObjectResult>(resultado);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, noProcesable.StatusCode);

        // COR-15/M8: la vacante se revalida antes de tocar el disco. Guardar primero y preguntar
        // despues es justo lo que dejaba el CV huerfano.
        await formularios.DidNotReceive().AlmacenarCvAsync(
            Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Enviar_con_token_desconocido_da_422_y_no_guarda_el_CV()
    {
        var formularios = Substitute.For<IJobFormsService>();

        var resultado = await Controlador(formularios)
            .Enviar(Guid.NewGuid(), Formulario(), Cv(), CancellationToken.None);

        Assert.IsType<UnprocessableEntityObjectResult>(resultado);

        await formularios.DidNotReceive().AlmacenarCvAsync(
            Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Enviar_rechazado_despues_de_guardar_el_CV_no_deja_el_archivo_en_disco()
    {
        var token = await ConEnlaceEnviadoAsync();

        // El CV se guarda porque la vacante esta abierta y la invitacion no esta completada, pero
        // recepcion.ProcesarAsync rechaza por falta de consentimiento (Regla 17) recien despues.
        var resultado = await Controlador()
            .Enviar(token, Formulario(consentimiento: false), Cv(), CancellationToken.None);

        Assert.IsType<UnprocessableEntityObjectResult>(resultado);
        Assert.Empty(ArchivosGuardados());
        Assert.Empty(await _entorno.Db.JobFormsRespuestas.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Enviar_con_invitacion_ya_completada_no_guarda_el_CV_y_responde_ya_recibido()
    {
        var token = await ConEnlaceEnviadoAsync();

        // Primer envio, sin CV: deja la invitacion Completado (V27).
        var primero = await Controlador().Enviar(token, Formulario(), cv: null, CancellationToken.None);
        Assert.IsType<OkObjectResult>(primero);

        // El Apps Script reintenta, esta vez con CV adjunto. La invitacion ya esta completada, asi
        // que ni se revalida la vacante (pudo cerrarse desde entonces) ni se guarda el archivo.
        var repetido = await Controlador().Enviar(token, Formulario(), Cv(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(repetido);
        var valor = Assert.IsAssignableFrom<object>(ok.Value);
        var yaRecibido = valor.GetType().GetProperty("YaRecibido")!.GetValue(valor);

        Assert.Equal(true, yaRecibido);
        Assert.Empty(ArchivosGuardados());
    }

    [Fact]
    public async Task Enviar_valido_guarda_el_CV_y_lo_deja_asociado_a_la_respuesta()
    {
        var token = await ConEnlaceEnviadoAsync();

        var resultado = await Controlador().Enviar(token, Formulario(), Cv(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(resultado);

        var respuesta = await _entorno.Db.JobFormsRespuestas.AsNoTracking().SingleAsync();
        Assert.NotNull(respuesta.CvUrl);
        Assert.Single(ArchivosGuardados());
    }

    public void Dispose() => _entorno.Dispose();
}
