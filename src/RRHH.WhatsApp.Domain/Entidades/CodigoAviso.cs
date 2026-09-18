using System.Globalization;
using System.Text;

namespace RRHH.WhatsApp.Domain.Entidades;

/// <summary>
/// El codigo corto que identifica una vacante en su aviso (A6, FUN-02). Va en el enlace
/// <c>wa.me/{numero}?text=Hola, postulo a K7M2QX</c>: el postulante llega con la vacante ya elegida
/// y el bot le manda el formulario sin pasarlo por el menu de ~20 empresas.
/// </summary>
public static class CodigoAviso
{
    /// <summary>
    /// Sin I, L ni O: se confunden con 1 y 0 cuando alguien copia el codigo a mano de un aviso
    /// impreso. Es el mismo alfabeto con el que la migracion genero los codigos existentes.
    /// </summary>
    public const string Alfabeto = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    /// <summary>Largo de los codigos nuevos. Con 31 simbolos hay 887 millones de combinaciones.</summary>
    public const int Largo = 6;

    private const int LargoMinimo = 4;
    private const int LargoMaximo = 12;

    public static string Generar(Random azar) =>
        string.Create(Largo, azar, (destino, r) =>
        {
            for (var i = 0; i < destino.Length; i++)
                destino[i] = Alfabeto[r.Next(Alfabeto.Length)];
        });

    /// <summary>
    /// Formato admitido, que es mas amplio que <see cref="Alfabeto"/>: los codigos tambien se pueden
    /// escribir a mano desde la administracion, y ahi vale cualquier letra o digito (FUN-20).
    /// </summary>
    public static bool EsValido(string? codigo) =>
        codigo is not null
        && codigo.Length is >= LargoMinimo and <= LargoMaximo
        && codigo.All(c => (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'));

    /// <summary>
    /// Lo que en un mensaje podria ser un codigo: cada palabra normalizada —mayusculas, sin tildes y
    /// sin signos— que cumpla el formato. Se devuelven todas porque el texto real es «Hola, postulo a
    /// K7M2QX»: el codigo es una palabra entre varias y no se sabe cual hasta consultarlas.
    /// </summary>
    public static IEnumerable<string> Candidatos(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            yield break;

        foreach (var palabra in Normalizar(texto).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (EsValido(palabra))
                yield return palabra;
        }
    }

    /// <summary>
    /// Mayusculas, sin tildes y con todo lo que no sea letra o digito convertido en separador. Tambien
    /// la usa el reconocimiento del nombre de la cuenta, para que «alicorp» y «Alicorp.» coincidan.
    /// </summary>
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return string.Empty;

        var descompuesto = texto.Normalize(NormalizationForm.FormD);
        var limpio = new StringBuilder(descompuesto.Length);

        foreach (var caracter in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caracter) == UnicodeCategory.NonSpacingMark)
                continue;

            limpio.Append(char.IsLetterOrDigit(caracter) ? char.ToUpperInvariant(caracter) : ' ');
        }

        return limpio.ToString().Trim();
    }
}
