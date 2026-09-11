using RRHH.WhatsApp.Contracts.Seguridad;

namespace RRHH.WhatsApp.Frontend.Servicios;

/// <summary>
/// La sesión del analista: quién es y con qué token habla con la Api (Sección 9.6.1).
/// <para>
/// Vive por circuito de Blazor, así que no se comparte entre pestañas ni entre usuarios. El token
/// no se persiste a propósito: cerrar el navegador cierra la sesión, que para una bandeja interna
/// es el comportamiento que menos sorprende.
/// </para>
/// </summary>
public sealed class SesionAnalista
{
    public int? AnalistaId { get; private set; }
    public string? Nombre { get; private set; }
    public string? Token { get; private set; }
    public DateTime? ExpiraUtc { get; private set; }

    /// <summary>Regla 4: el rol Sistemas ve todas las conversaciones, no solo las suyas.</summary>
    public bool EsSistemas { get; private set; }

    /// <summary>Hay sesión y el token todavía sirve.</summary>
    public bool Activa => Token is not null && ExpiraUtc > DateTime.UtcNow;

    public event Action? Cambio;

    public void Iniciar(SesionIniciada sesion)
    {
        AnalistaId = sesion.AnalistaId;
        Nombre = sesion.Nombre;
        Token = sesion.Token;
        ExpiraUtc = sesion.ExpiraUtc;
        EsSistemas = string.Equals(sesion.Rol, "Sistemas", StringComparison.OrdinalIgnoreCase);

        Cambio?.Invoke();
    }

    public void Cerrar()
    {
        AnalistaId = null;
        Nombre = null;
        Token = null;
        ExpiraUtc = null;
        EsSistemas = false;

        Cambio?.Invoke();
    }
}
