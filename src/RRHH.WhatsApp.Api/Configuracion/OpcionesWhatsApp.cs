namespace RRHH.WhatsApp.Api.Configuracion;

/// <summary>
/// El numero publico de WhatsApp: al que escriben los postulantes y el que va en el enlace de cada
/// aviso (FUN-02, A6).
/// <para>
/// No es un secreto —se imprime en los avisos— pero tampoco es el <c>PhoneNumberId</c> de Meta, que
/// es interno y va por variable de entorno con el resto de las credenciales.
/// </para>
/// </summary>
public sealed class OpcionesWhatsApp
{
    public const string Seccion = "WhatsApp";

    /// <summary>En formato internacional, como <c>+51999888777</c>. Vacio significa sin configurar.</summary>
    public string NumeroPublico { get; set; } = string.Empty;

    /// <summary>
    /// El enlace que lleva al postulante a escribir con la vacante ya elegida. Nulo sin numero
    /// configurado: la pantalla lo avisa en vez de ofrecer un enlace que no lleva a ninguna parte.
    /// </summary>
    public string? EnlaceDeAviso(string codigo)
    {
        var digitos = SoloDigitos(NumeroPublico);

        if (digitos.Length == 0 || string.IsNullOrWhiteSpace(codigo))
            return null;

        // wa.me quiere el numero sin «+» ni separadores, y el texto ya escrito: el postulante solo
        // toca enviar, y el bot reconoce el codigo sin pasarlo por el menu de ~20 empresas.
        return $"https://wa.me/{digitos}?text={Uri.EscapeDataString($"Hola, postulo a {codigo}")}";
    }

    /// <summary>
    /// El numero llega escrito por una persona («+1 (555) 665-0366»), asi que se limpia todo lo que
    /// no sea digito antes de armar la URL.
    /// </summary>
    private static string SoloDigitos(string? numero) =>
        string.Concat((numero ?? string.Empty).Where(char.IsAsciiDigit));
}
