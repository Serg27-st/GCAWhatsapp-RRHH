using System.Text.Json;
using RRHH.WhatsApp.Api.Controllers;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// El contrato entre el Apps Script del formulario y la Api (V27). El script vive fuera de la
/// solución —en Google— y nadie lo compila con el proyecto: si alguien renombra un campo del lado
/// de la Api, esto es lo único que avisa antes de que se rompa en producción.
/// <para>
/// El JSON de abajo es el que arma <c>scripts/apps-script/Codigo.gs</c>.
/// </para>
/// </summary>
public class ContratoAppsScriptTests
{
    /// <summary>Las mismas opciones que usa ASP.NET Core para leer el cuerpo de la peticion.</summary>
    private static readonly JsonSerializerOptions ComoLaApi = new(JsonSerializerDefaults.Web);

    private const string EnvioDelScript = """
        {
          "token": "6f1e6d0c2b5f4a1e8f0d9c3b7a2e5d41",
          "dni": "45678912",
          "nombreCompleto": "Maria Quispe",
          "telefonoE164": "+51987654321",
          "email": "maria@correo.pe",
          "cvUrl": "https://drive.google.com/file/d/abc123/view",
          "consentimientoAceptado": true,
          "datosJson": "{\"Anos de experiencia\":\"2\"}"
        }
        """;

    private static JobFormsController.EnvioGoogleForms Leer(string json) =>
        JsonSerializer.Deserialize<JobFormsController.EnvioGoogleForms>(json, ComoLaApi)!;

    [Fact]
    public void La_Api_entiende_el_envio_que_arma_el_script()
    {
        var envio = Leer(EnvioDelScript);

        // El token viaja sin guiones, como lo arma EnlaceJobForms para la URL del formulario, y
        // por eso se recibe como texto: System.Text.Json solo entiende la forma con guiones.
        Assert.True(envio.TokenValido(out var token));
        Assert.Equal(Guid.Parse("6f1e6d0c-2b5f-4a1e-8f0d-9c3b7a2e5d41"), token);
        Assert.Equal("45678912", envio.Dni);
        Assert.Equal("Maria Quispe", envio.NombreCompleto);
        Assert.Equal("+51987654321", envio.TelefonoE164);
        Assert.Equal("maria@correo.pe", envio.Email);
        Assert.Equal("https://drive.google.com/file/d/abc123/view", envio.CvUrl);
        Assert.True(envio.ConsentimientoAceptado);

        // Los campos opcionales de la vacante viajan como JSON dentro del campo, sin que ni el
        // script ni la Api tengan que conocerlos (Seccion 9.2).
        Assert.Contains("Anos de experiencia", envio.DatosJson);
    }

    /// <summary>
    /// Lo que manda <c>probarConfiguracion()</c> al instalar el script: alcanza para que la Api lo
    /// entienda y lo rechace por el token, que es la señal de que URL y secreto estan bien.
    /// </summary>
    [Fact]
    public void La_prueba_de_configuracion_del_script_tambien_se_entiende()
    {
        var envio = Leer("""
            {
              "token": "00000000-0000-0000-0000-000000000000",
              "dni": "00000000",
              "consentimientoAceptado": false
            }
            """);

        Assert.True(envio.TokenValido(out var token));
        Assert.Equal(Guid.Empty, token);
        Assert.Null(envio.CvUrl);
        Assert.False(envio.ConsentimientoAceptado);
    }

    /// <summary>Un formulario sin adjunto ni campos opcionales: el script manda nulos, no cadenas vacias.</summary>
    [Fact]
    public void Un_envio_minimo_sigue_siendo_valido()
    {
        var envio = Leer("""
            {
              "token": "6f1e6d0c2b5f4a1e8f0d9c3b7a2e5d41",
              "dni": "45678912",
              "nombreCompleto": null,
              "telefonoE164": null,
              "email": null,
              "cvUrl": null,
              "consentimientoAceptado": true,
              "datosJson": "{}"
            }
            """);

        Assert.Equal("45678912", envio.Dni);
        Assert.Null(envio.NombreCompleto);
        Assert.Equal("{}", envio.DatosJson);
    }

    /// <summary>Lo que no es un token no puede llegar al dominio: se rechaza con su motivo, no con un 500.</summary>
    [Fact]
    public void Un_token_que_no_lo_es_se_rechaza()
    {
        var envio = Leer("""
            {
              "token": "no-es-un-token",
              "dni": "45678912",
              "consentimientoAceptado": true
            }
            """);

        Assert.False(envio.TokenValido(out _));
    }

    /// <summary>
    /// Encuentra la raiz de la solucion subiendo desde donde corren las pruebas, hasta dar con el
    /// .sln. El script vive fuera de <c>tests/</c> (en <c>scripts/apps-script/</c>), asi que no hay
    /// una ruta relativa fija que funcione sin depender de donde el runner ubique el binario.
    /// </summary>
    private static string RutaRaizSolucion()
    {
        var directorio = new DirectoryInfo(AppContext.BaseDirectory);

        while (directorio is not null &&
               !File.Exists(Path.Combine(directorio.FullName, "RRHH.WhatsApp.sln")))
        {
            directorio = directorio.Parent;
        }

        if (directorio is null)
        {
            throw new InvalidOperationException(
                "No se encontro RRHH.WhatsApp.sln subiendo desde " + AppContext.BaseDirectory);
        }

        return directorio.FullName;
    }

    private static string LeerCodigoDelScript() => File.ReadAllText(
        Path.Combine(RutaRaizSolucion(), "scripts", "apps-script", "Codigo.gs"));

    /// <summary>
    /// Contrato textual, no ejecutable (COR-15/T0.06): el script vive en Google y no corre dentro de
    /// .NET, asi que esto no reemplaza probarlo en Apps Script. Lo que evita es que alguien retroceda
    /// el manejo del 429 sin darse cuenta —un limite de velocidad no es un rechazo del negocio, y
    /// tratarlo como uno le haria perder postulaciones a una campana con muchos envios a la vez.
    /// </summary>
    [Fact]
    public void El_script_reintenta_el_429_antes_de_tratarlo_como_rechazo_y_respeta_Retry_After()
    {
        var codigo = LeerCodigoDelScript();

        Assert.Contains("const REINTENTOS = 5;", codigo);
        Assert.Contains("Retry-After", codigo);
        Assert.Contains("ESPERA_MAXIMA_MS", codigo);

        var indiceManejo429 = codigo.IndexOf("codigo === 429", StringComparison.Ordinal);
        var indiceRechazo4xx = codigo.IndexOf("codigo >= 400 && codigo < 500", StringComparison.Ordinal);

        Assert.True(indiceManejo429 >= 0, "El script deberia distinguir el 429 explicitamente.");
        Assert.True(indiceRechazo4xx >= 0, "El script deberia seguir rechazando el resto de los 4xx.");
        Assert.True(
            indiceManejo429 < indiceRechazo4xx,
            "El 429 tiene que resolverse antes de la rama que trata todo 4xx como rechazo definitivo.");

        // Entre el chequeo del 429 y el de "el resto de los 4xx" no puede haber un throw: si lo
        // hubiera, el 429 seguiria siendo un rechazo definitivo pese a distinguirse del resto.
        var bloque429 = codigo[indiceManejo429..indiceRechazo4xx];
        Assert.DoesNotContain("throw new Error", bloque429);
    }
}
