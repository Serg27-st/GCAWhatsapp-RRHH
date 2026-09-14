using RRHH.WhatsApp.Domain.Entidades;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// Regla 9 — el enlace que el bot le manda al postulante. Lo que se prueba es que el token llegue
/// al formulario: sin el, el envio no se puede atribuir a ninguna postulacion (V27).
/// </summary>
public class EnlaceJobFormsTests
{
    private static readonly Guid Token = Guid.Parse("6f1e6d0c-2b5f-4a1e-8f0d-9c3b7a2e5d41");

    /// <summary>
    /// Google Forms solo prellena parametros con la forma entry.&lt;id&gt;=, distinta en cada
    /// formulario: por eso la URL de la vacante dice donde va el token.
    /// </summary>
    [Fact]
    public void Con_marcador_el_token_va_donde_lo_pide_la_vacante()
    {
        var enlace = EnlaceJobForms.Construir(
            "https://docs.google.com/forms/d/e/XYZ/viewform?usp=pp_url&entry.123456={token}", Token);

        Assert.Equal(
            "https://docs.google.com/forms/d/e/XYZ/viewform?usp=pp_url&entry.123456=6f1e6d0c2b5f4a1e8f0d9c3b7a2e5d41",
            enlace);
    }

    /// <summary>Sin marcador se agrega el parametro propio, que es lo que leera el formulario de Razor Pages.</summary>
    [Fact]
    public void Sin_marcador_se_agrega_el_parametro_propio()
    {
        Assert.Equal(
            $"https://rrhh.empresa.pe/postular?{EnlaceJobForms.Parametro}={Token:N}",
            EnlaceJobForms.Construir("https://rrhh.empresa.pe/postular", Token));
    }

    [Fact]
    public void Con_query_previa_el_parametro_se_agrega_sin_romperla()
    {
        Assert.Equal(
            $"https://rrhh.empresa.pe/postular?vacante=7&{EnlaceJobForms.Parametro}={Token:N}",
            EnlaceJobForms.Construir("https://rrhh.empresa.pe/postular?vacante=7", Token));
    }

    /// <summary>Regla 20 en espiritu: mejor no mandar nada que mandar un enlace roto.</summary>
    [Fact]
    public void Una_vacante_sin_formulario_no_produce_enlace()
    {
        Assert.Null(EnlaceJobForms.Construir(null, Token));
        Assert.Null(EnlaceJobForms.Construir("   ", Token));
    }
}
