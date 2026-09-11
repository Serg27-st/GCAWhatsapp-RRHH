using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Infrastructure.Servicios;

/// <summary>
/// Verifica credenciales de analistas (Sección 9.6.1).
/// <para>
/// Las contraseñas se guardan con PBKDF2-SHA256 y sal por usuario. No se usa un hash rápido
/// —SHA256 a secas— a propósito: la lentitud es la defensa, porque es lo que hace impracticable
/// probar millones de candidatas si alguna vez se filtra la tabla.
/// </para>
/// </summary>
public sealed class AutenticacionService(
    RrhhDbContext db,
    ILogger<AutenticacionService> log) : IAutenticacionService
{
    private const int TamanoSal = 16;
    private const int TamanoClave = 32;

    /// <summary>
    /// Iteraciones de PBKDF2. Cuesta unos milisegundos por intento, que no se nota al entrar y sí
    /// se nota al intentar adivinar. Se guarda en el hash para poder subirlo después sin invalidar
    /// las contraseñas ya existentes.
    /// </summary>
    private const int Iteraciones = 210_000;

    public async Task<ResultadoAutenticacion> VerificarAsync(
        string email, string contrasena, CancellationToken ct = default)
    {
        var analista = await db.Analistas
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Email == email, ct);

        // Un solo mensaje para "no existe", "está inactivo" y "contraseña incorrecta". Distinguirlos
        // le diría a quien prueba credenciales cuáles de los correos son reales.
        var generico = new ResultadoAutenticacion(false, "Credenciales incorrectas.", 0, null, null);

        if (analista is null || !analista.Activo || analista.HashContrasena is null)
        {
            // Se gasta el mismo tiempo igual: responder al instante cuando el usuario no existe
            // delata cuáles sí, aunque el mensaje sea el mismo.
            Verificar(contrasena, HashFalso());

            return generico;
        }

        if (!Verificar(contrasena, analista.HashContrasena))
        {
            log.LogWarning("Intento de inicio de sesión fallido para {Email}.", email);

            return generico;
        }

        return new ResultadoAutenticacion(
            true, null, analista.AnalistaId, analista.Nombre, analista.Rol.ToString());
    }

    public async Task EstablecerContrasenaAsync(
        int analistaId, string contrasena, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(contrasena) || contrasena.Length < 10)
            throw new InvalidOperationException("La contraseña necesita al menos 10 caracteres.");

        var analista = await db.Analistas.FirstOrDefaultAsync(a => a.AnalistaId == analistaId, ct)
            ?? throw new InvalidOperationException($"No existe el analista {analistaId}.");

        analista.HashContrasena = Hashear(contrasena);
        analista.FechaContrasena = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        log.LogInformation("Contraseña establecida para el analista {AnalistaId}.", analistaId);
    }

    public Task<bool> SinContrasenasAsync(CancellationToken ct = default) =>
        db.Analistas.AllAsync(a => a.HashContrasena == null, ct);

    /// <summary>Formato: iteraciones.sal.clave, todo en base64 salvo las iteraciones.</summary>
    private static string Hashear(string contrasena)
    {
        var sal = RandomNumberGenerator.GetBytes(TamanoSal);

        var clave = KeyDerivation.Pbkdf2(
            contrasena, sal, KeyDerivationPrf.HMACSHA256, Iteraciones, TamanoClave);

        return $"{Iteraciones}.{Convert.ToBase64String(sal)}.{Convert.ToBase64String(clave)}";
    }

    private static bool Verificar(string contrasena, string hash)
    {
        var partes = hash.Split('.');

        if (partes.Length != 3 || !int.TryParse(partes[0], out var iteraciones))
            return false;

        try
        {
            var sal = Convert.FromBase64String(partes[1]);
            var esperada = Convert.FromBase64String(partes[2]);

            var calculada = KeyDerivation.Pbkdf2(
                contrasena, sal, KeyDerivationPrf.HMACSHA256, iteraciones, esperada.Length);

            // Comparación de tiempo fijo: una comparación normal filtra la clave por el reloj.
            return CryptographicOperations.FixedTimeEquals(calculada, esperada);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Hash descartable, solo para que un usuario inexistente cueste lo mismo que uno real.</summary>
    private static string HashFalso() => Hashear("no-importa");
}
