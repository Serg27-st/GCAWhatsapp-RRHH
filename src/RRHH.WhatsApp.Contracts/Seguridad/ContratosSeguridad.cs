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

/// <summary>Alta de la primera contraseña, solo mientras no exista ninguna.</summary>
public sealed record PeticionArranque(string Email, string Contrasena);
