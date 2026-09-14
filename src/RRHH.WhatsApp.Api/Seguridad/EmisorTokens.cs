using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Api.Seguridad;

/// <summary>Claims propios. Constantes para que emisor y lectores no se separen por una cadena.</summary>
public static class ClaimsAnalista
{
    /// <summary>
    /// El id del analista. Es el claim que reemplaza al parámetro <c>analistaId</c> que antes
    /// viajaba en la URL: mientras lo mandaba el cliente, la Regla 4 era decorativa.
    /// </summary>
    public const string AnalistaId = "analista_id";

    public const string Rol = ClaimTypes.Role;

    /// <summary>Regla 4: el rol Sistemas ve todas las conversaciones, para soporte y auditoría.</summary>
    public const string RolSistemas = "Sistemas";

    /// <summary>V23: métricas, ausencias de otros y cobertura de cuentas, sin ver conversaciones ajenas.</summary>
    public const string RolJefatura = "Jefatura";
}

public sealed class EmisorTokens(IOptions<OpcionesJwt> opciones)
{
    private readonly OpcionesJwt _opciones = opciones.Value;

    public (string Token, DateTime ExpiraUtc) Emitir(ResultadoAutenticacion analista)
    {
        var expira = DateTime.UtcNow.AddHours(_opciones.VigenciaHoras);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, analista.AnalistaId.ToString()),
            new(ClaimsAnalista.AnalistaId, analista.AnalistaId.ToString()),
            new(ClaimTypes.Name, analista.Nombre ?? string.Empty),
            new(ClaimsAnalista.Rol, analista.Rol ?? "Analista"),
            // Identifica este token en particular: es lo que permitiría revocarlo si algún día
            // hace falta una lista de tokens anulados.
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var credenciales = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opciones.Clave)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _opciones.Emisor,
            audience: _opciones.Audiencia,
            claims: claims,
            expires: expira,
            signingCredentials: credenciales);

        return (new JwtSecurityTokenHandler().WriteToken(token), expira);
    }
}

/// <summary>Lee el analista del token en vez de creerle al cliente.</summary>
public static class ExtensionesClaims
{
    /// <summary>
    /// Id del analista autenticado. Lanza si no está: un endpoint con <c>[Authorize]</c> siempre
    /// lo tiene, y si faltara es un error de configuración que conviene ver, no tapar con un 0.
    /// </summary>
    public static int AnalistaId(this ClaimsPrincipal usuario)
    {
        var valor = usuario.FindFirst(ClaimsAnalista.AnalistaId)?.Value;

        return int.TryParse(valor, out var id)
            ? id
            : throw new InvalidOperationException("El token no trae el analista.");
    }

    public static bool EsSistemas(this ClaimsPrincipal usuario) =>
        usuario.IsInRole(ClaimsAnalista.RolSistemas);
}
