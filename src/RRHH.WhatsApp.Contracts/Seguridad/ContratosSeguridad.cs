namespace RRHH.WhatsApp.Contracts.Seguridad;

public sealed record PeticionLogin(string Email, string Contrasena);

/// <summary>Lo que la bandeja guarda tras entrar. El token viaja en cada llamada siguiente.</summary>
public sealed record SesionIniciada(
    string Token,
    DateTime ExpiraUtc,
    int AnalistaId,
    string Nombre,
    string Rol);

public sealed record PeticionCambiarContrasena(string ContrasenaActual, string ContrasenaNueva);

/// <summary>
/// Alta de la primera contraseña, solo mientras no exista ninguna. En una base nueva, si el correo
/// es el configurado en el servidor, tambien da de alta al analista de Sistemas con ese nombre (V25).
/// </summary>
public sealed record PeticionArranque(string Email, string Contrasena, string? Nombre = null);

/// <summary>Sistemas restablece la contraseña de otro analista. El analista va en la ruta.</summary>
public sealed record PeticionRestablecerContrasena(string Contrasena);
