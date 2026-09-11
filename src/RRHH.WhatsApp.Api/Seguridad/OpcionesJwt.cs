namespace RRHH.WhatsApp.Api.Seguridad;

/// <summary>
/// Firma y vigencia de los tokens (Sección 9.6.1).
/// <para>
/// Se eligió JWT propio y no el directorio corporativo porque la empresa no tiene hoy un AD/SSO
/// al que integrarse; queda como interfaz reemplazable si mañana lo hay.
/// </para>
/// </summary>
public sealed class OpcionesJwt
{
    public const string Seccion = "Jwt";

    /// <summary>
    /// Clave de firma. Llega por variable de entorno <c>Jwt__Clave</c>: quien la tenga puede
    /// emitir tokens válidos, así que nunca se versiona. Mínimo 32 caracteres.
    /// </summary>
    public string Clave { get; set; } = string.Empty;

    public string Emisor { get; set; } = "RRHH.WhatsApp";
    public string Audiencia { get; set; } = "RRHH.WhatsApp.Bandeja";

    /// <summary>
    /// Cuánto dura el token. Una jornada por defecto: más corto obliga al analista a reingresar en
    /// medio de la atención, y más largo alarga la ventana si alguien se lleva el token.
    /// </summary>
    public int VigenciaHoras { get; set; } = 9;

    public bool EstaConfigurada => Clave.Length >= 32;
}
