using RRHH.WhatsApp.Api.Configuracion;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// COR-15/M8: el analista termina abriendo <c>CvUrl</c> a ciegas, asi que no alcanza con "es una
/// URL". Tiene que ser https y su host tiene que coincidir EXACTO con uno de
/// <c>JobForms:DominiosCvPermitidos</c> — no <c>EndsWith</c> ni <c>Contains</c>, que son los que
/// dejan pasar los ejemplos maliciosos de aca abajo.
/// </summary>
public class ValidadorCvUrlTests
{
    private static readonly string[] Dominios = ["drive.google.com", "docs.google.com"];

    [Fact]
    public void Nulo_o_vacio_se_permite_porque_el_CV_no_es_obligatorio_aca()
    {
        Assert.True(ValidadorCvUrl.EsValida(null, Dominios));
        Assert.True(ValidadorCvUrl.EsValida("", Dominios));
        Assert.True(ValidadorCvUrl.EsValida("   ", Dominios));
    }

    [Fact]
    public void Un_enlace_de_Drive_es_valido()
    {
        Assert.True(ValidadorCvUrl.EsValida("https://drive.google.com/file/d/abc123/view", Dominios));
    }

    [Fact]
    public void Http_sin_cifrar_se_rechaza_aunque_el_host_sea_valido()
    {
        Assert.False(ValidadorCvUrl.EsValida("http://drive.google.com/file/d/abc123/view", Dominios));
    }

    [Fact]
    public void Un_subdominio_falso_que_termina_como_el_permitido_se_rechaza()
    {
        Assert.False(ValidadorCvUrl.EsValida("https://drive.google.com.evil.com/x", Dominios));
    }

    [Fact]
    public void Un_dominio_ajeno_con_el_permitido_en_la_ruta_se_rechaza()
    {
        Assert.False(ValidadorCvUrl.EsValida("https://evil.com/drive.google.com", Dominios));
    }

    [Fact]
    public void Un_userinfo_que_simula_el_dominio_permitido_se_rechaza()
    {
        Assert.False(ValidadorCvUrl.EsValida("https://drive.google.com@evil.com/x", Dominios));
    }

    [Fact]
    public void Un_esquema_que_no_es_http_se_rechaza()
    {
        Assert.False(ValidadorCvUrl.EsValida("javascript:alert(1)", Dominios));
    }

    [Fact]
    public void Una_url_relativa_se_rechaza()
    {
        Assert.False(ValidadorCvUrl.EsValida("/file/d/abc123/view", Dominios));
    }

    [Fact]
    public void Un_texto_que_no_es_url_se_rechaza()
    {
        Assert.False(ValidadorCvUrl.EsValida("no-es-una-url", Dominios));
    }
}
