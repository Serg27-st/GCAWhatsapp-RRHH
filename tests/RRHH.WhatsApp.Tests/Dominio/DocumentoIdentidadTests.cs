using RRHH.WhatsApp.Domain.Entidades;

namespace RRHH.WhatsApp.Tests.Dominio;

/// <summary>
/// COR-15/M8: el DNI que llega del JobForms nunca debia tumbar el webhook con un 500. Esta clase
/// prueba solo la normalizacion en si, sin base de datos ni controlador — es la razon por la que
/// vive en Domain y no en la Api.
/// </summary>
public class DocumentoIdentidadTests
{
    [Fact]
    public void Un_DNI_de_8_digitos_es_valido()
    {
        Assert.True(DocumentoIdentidad.TryNormalizar("45678912", out var normalizado));
        Assert.Equal("45678912", normalizado);
    }

    [Fact]
    public void Un_DNI_con_espacios_se_limpia_y_es_valido()
    {
        Assert.True(DocumentoIdentidad.TryNormalizar(" 4567 8912 ", out var normalizado));
        Assert.Equal("45678912", normalizado);
    }

    [Fact]
    public void Un_DNI_con_guiones_y_puntos_se_limpia_y_es_valido()
    {
        Assert.True(DocumentoIdentidad.TryNormalizar("45.678-912", out var normalizado));
        Assert.Equal("45678912", normalizado);
    }

    [Fact]
    public void Siete_digitos_no_es_un_DNI_valido()
    {
        Assert.False(DocumentoIdentidad.TryNormalizar("4567891", out _));
    }

    [Fact]
    public void Ocho_caracteres_con_una_letra_no_es_valido()
    {
        // Ni cae en el rango de 8 digitos exactos (tiene una letra) ni en el de 9 a 12 del carne
        // (tiene solo 8 caracteres): no es ninguno de los dos documentos.
        Assert.False(DocumentoIdentidad.TryNormalizar("4567891A", out _));
    }

    [Theory]
    [InlineData("abc123456")]
    [InlineData("ABC123456")]
    public void Un_carne_de_extranjeria_alfanumerico_de_9_a_12_caracteres_es_valido(string valor)
    {
        Assert.True(DocumentoIdentidad.TryNormalizar(valor, out var normalizado));
        Assert.Equal("ABC123456", normalizado);
    }

    [Fact]
    public void Un_carne_de_12_caracteres_es_valido()
    {
        Assert.True(DocumentoIdentidad.TryNormalizar("AB123456789", out var normalizado));
        Assert.Equal("AB123456789", normalizado);
    }

    [Fact]
    public void Trece_caracteres_no_es_valido()
    {
        Assert.False(DocumentoIdentidad.TryNormalizar("AB12345678901", out _));
    }

    [Fact]
    public void Nulo_no_es_valido()
    {
        Assert.False(DocumentoIdentidad.TryNormalizar(null, out var normalizado));
        Assert.Equal(string.Empty, normalizado);
    }

    [Fact]
    public void Vacio_o_solo_espacios_no_es_valido()
    {
        Assert.False(DocumentoIdentidad.TryNormalizar("   ", out _));
        Assert.False(DocumentoIdentidad.TryNormalizar("", out _));
    }

    [Fact]
    public void Caracteres_que_no_son_letras_ni_digitos_no_son_validos()
    {
        Assert.False(DocumentoIdentidad.TryNormalizar("ABC@12345", out _));
        Assert.False(DocumentoIdentidad.TryNormalizar("1234/5678", out _));
    }
}
