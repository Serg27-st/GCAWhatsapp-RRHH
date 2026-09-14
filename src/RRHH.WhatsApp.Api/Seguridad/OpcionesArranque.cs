namespace RRHH.WhatsApp.Api.Seguridad;

/// <summary>
/// Arranque de una base nueva (V25). El correo llega por la variable de entorno
/// <c>Arranque__EmailSistemas</c>. No es un secreto: acota quien puede darse de alta como Sistemas
/// mientras la puerta del arranque sigue abierta, que se cierra sola con la primera contraseña.
/// </summary>
public sealed class OpcionesArranque
{
    public const string Seccion = "Arranque";

    /// <summary>Correo del primer analista de Sistemas. Vacio: el arranque no crea a nadie, solo fija contraseñas.</summary>
    public string EmailSistemas { get; set; } = string.Empty;

    /// <summary>Si ese correo es el que Sistemas dejo configurado. Sin configurar, no admite ninguno.</summary>
    public bool Admite(string email) =>
        !string.IsNullOrWhiteSpace(EmailSistemas)
        && string.Equals(EmailSistemas.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase);
}
