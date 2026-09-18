using RRHH.WhatsApp.Domain.Entidades;

namespace RRHH.WhatsApp.Tests.Dominio;

/// <summary>
/// FUN-02 (A6): el codigo del aviso es lo que le ahorra al postulante el menu de ~20 empresas. Se
/// prueba aparte porque el reconocimiento corre sobre texto que escribe una persona: con tildes, con
/// signos y con el codigo en medio de una frase.
/// </summary>
public class CodigoAvisoTests
{
    [Fact]
    public void El_codigo_generado_usa_solo_el_alfabeto_sin_letras_confundibles()
    {
        var azar = new Random(1);

        for (var i = 0; i < 200; i++)
        {
            var codigo = CodigoAviso.Generar(azar);

            Assert.Equal(CodigoAviso.Largo, codigo.Length);
            Assert.All(codigo, c => Assert.Contains(c, CodigoAviso.Alfabeto));
            Assert.DoesNotContain(codigo, c => c is 'I' or 'L' or 'O');
        }
    }

    [Theory]
    [InlineData("K7M2QX")]
    [InlineData("ABCD")]
    [InlineData("A1B2C3D4E5F6")]
    public void Un_codigo_bien_formado_es_valido(string codigo)
    {
        Assert.True(CodigoAviso.EsValido(codigo));
    }

    [Theory]
    [InlineData("ABC")]
    [InlineData("A1B2C3D4E5F6G")]
    [InlineData("k7m2qx")]
    [InlineData("K7M-2Q")]
    [InlineData(null)]
    public void Lo_que_no_tiene_el_formato_no_es_valido(string? codigo)
    {
        Assert.False(CodigoAviso.EsValido(codigo));
    }

    /// <summary>El enlace del aviso deja escrito «Hola, postulo a K7M2QX»: el codigo viene en una frase.</summary>
    [Fact]
    public void El_codigo_se_reconoce_dentro_de_la_frase_del_enlace()
    {
        Assert.Contains("K7M2QX", CodigoAviso.Candidatos("Hola, postulo a K7M2QX"));
    }

    /// <summary>
    /// Se descartan solo las palabras que no tienen el formato. Que «HOLA» quede como candidata no
    /// molesta: lo resuelve la consulta, que no encuentra ninguna vacante con ese codigo.
    /// </summary>
    [Fact]
    public void Las_palabras_mas_cortas_que_un_codigo_no_son_candidatas()
    {
        var candidatos = CodigoAviso.Candidatos("Hola, ¿hay trabajo?").ToList();

        Assert.DoesNotContain("HAY", candidatos);
        Assert.Equal(["HOLA", "TRABAJO"], candidatos);
    }

    [Fact]
    public void El_texto_se_normaliza_a_mayusculas_sin_tildes_ni_signos()
    {
        Assert.Equal("POSTULO A K7M2QX", CodigoAviso.Normalizar("¡Postulo a k7m2qx!"));
        Assert.Equal("INTRADEVCO", CodigoAviso.Normalizar(" intradevco "));
        Assert.Equal("ATENCION", CodigoAviso.Normalizar("Atención"));
    }

    [Fact]
    public void Un_texto_vacio_no_da_candidatos()
    {
        Assert.Empty(CodigoAviso.Candidatos("   "));
        Assert.Equal(string.Empty, CodigoAviso.Normalizar(null));
    }
}
