using RRHH.WhatsApp.Contracts.Seguridad;

namespace RRHH.WhatsApp.Frontend.Servicios;

/// <summary>
/// La sesión del analista: quién es y con qué token habla con la Api (Sección 9.6.1).
/// <para>
/// Vive por circuito de Blazor, así que no se comparte entre pestañas ni entre usuarios. El token
/// no se persiste a propósito: cerrar el navegador cierra la sesión, que para una bandeja interna
/// es el comportamiento que menos sorprende.
/// </para>
/// <para>
/// Los roles acá solo deciden qué se ofrece en pantalla. Quien permite o rechaza es la Api (V23).
/// </para>
/// </summary>
public sealed class SesionAnalista
{
    public int? AnalistaId { get; private set; }
    public string? Nombre { get; private set; }
    public string? Token { get; private set; }
    public DateTime? ExpiraUtc { get; private set; }

    /// <summary>Rol tal como lo emite la Api: Analista, Jefatura o Sistemas.</summary>
    public string? Rol { get; private set; }

    /// <summary>Regla 4: el rol Sistemas ve todas las conversaciones, no solo las suyas.</summary>
    public bool EsSistemas => EsRol("Sistemas");

    /// <summary>V23: la jefatura del área mira el panel y la cobertura, sin ver conversaciones ajenas.</summary>
    public bool EsJefatura => EsRol("Jefatura");

    /// <summary>Regla 18: el panel de gerencia es de Jefatura y de Sistemas.</summary>
    public bool VeMetricas => EsSistemas || EsJefatura;

    /// <summary>V23: la cobertura de las cuentas y las ausencias de otros son de Jefatura y Sistemas.</summary>
    public bool PuedeAdministrarEquipo => EsSistemas || EsJefatura;

    /// <summary>Hay sesión y el token todavía sirve.</summary>
    public bool Activa => Token is not null && ExpiraUtc > DateTime.UtcNow;

    public event Action? Cambio;

    public void Iniciar(SesionIniciada sesion)
    {
        AnalistaId = sesion.AnalistaId;
        Nombre = sesion.Nombre;
        Token = sesion.Token;
        ExpiraUtc = sesion.ExpiraUtc;
        Rol = sesion.Rol;

        Cambio?.Invoke();
    }

    public void Cerrar()
    {
        AnalistaId = null;
        Nombre = null;
        Token = null;
        ExpiraUtc = null;
        Rol = null;

        Cambio?.Invoke();
    }

    private bool EsRol(string rol) => string.Equals(Rol, rol, StringComparison.OrdinalIgnoreCase);
}
