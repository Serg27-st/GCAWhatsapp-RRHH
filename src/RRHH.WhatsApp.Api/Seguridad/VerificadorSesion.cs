using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Api.Seguridad;

/// <summary>
/// ARQ-11 (M10, V34): comprueba, en cada petición, que el token siga valiendo.
/// <para>
/// Un token vive hasta nueve horas. Sin esto, darle de baja a alguien o restablecerle la contraseña
/// —que es lo que se hace cuando una cuenta pudo quedar comprometida— no le quita el acceso hasta que
/// su token vence solo. La firma sigue siendo válida; lo que cambia es que ya no corresponde.
/// </para>
/// <para>
/// El estado se cachea 60 s: consultarlo en cada petición de cada analista sería una consulta más por
/// request para una respuesta que casi nunca cambia. La contrapartida es esa ventana, y por eso quien
/// cierra sesiones limpia la entrada con <see cref="ClaveCache"/>.
/// </para>
/// </summary>
public sealed class VerificadorSesion(IMemoryCache cache)
{
    /// <summary>Cuánto dura el estado cacheado. Es la ventana en la que un token revocado todavía entra.</summary>
    public static readonly TimeSpan Vigencia = TimeSpan.FromSeconds(60);

    public static string ClaveCache(int analistaId) => $"seg:{analistaId}";

    /// <summary>Devuelve el motivo del rechazo, o <c>null</c> si el token sigue valiendo.</summary>
    public async Task<string?> RevisarAsync(
        ClaimsPrincipal usuario, IAnalistaService analistas, CancellationToken ct = default)
    {
        if (!int.TryParse(usuario.FindFirst(ClaimsAnalista.AnalistaId)?.Value, out var analistaId))
            return "El token no trae el analista.";

        // Sin version no se puede saber si el token quedo atras: es de antes de ARQ-11 y no entra.
        if (!int.TryParse(usuario.FindFirst(ClaimsAnalista.VersionSeguridad)?.Value, out var version))
            return "El token no trae la version de seguridad.";

        var estado = await cache.GetOrCreateAsync(ClaveCache(analistaId), async entrada =>
        {
            entrada.AbsoluteExpirationRelativeToNow = Vigencia;

            return await analistas.ObtenerEstadoSeguridadAsync(analistaId, ct);
        });

        if (estado is null)
            return "El analista ya no existe.";

        if (!estado.Activo)
            return "El analista ya no esta activo.";

        return version == estado.Version ? null : "La sesion fue cerrada. Volve a entrar.";
    }
}
