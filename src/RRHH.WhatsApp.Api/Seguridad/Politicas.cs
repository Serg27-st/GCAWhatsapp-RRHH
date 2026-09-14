using Microsoft.AspNetCore.Authorization;

namespace RRHH.WhatsApp.Api.Seguridad;

/// <summary>
/// Quien administra que (V23). Los nombres son constantes para que los controladores y el registro
/// no se separen por una cadena mal escrita.
/// <para>
/// Se suman a la politica de respaldo que exige sesion, no la reemplazan. Lo que depende del
/// recurso —de que cuenta es la vacante, de quien es la ausencia— no cabe en una politica por rol y
/// se decide en el controlador.
/// </para>
/// </summary>
public static class Politicas
{
    /// <summary>La estructura del sistema: cuentas, analistas, horario y parametros de reglas. Solo Sistemas.</summary>
    public const string Estructura = "Estructura";

    /// <summary>
    /// Lo de la jefatura del area: quien cubre cada cuenta, las ausencias de otros y el panel de la
    /// Regla 18. Jefatura y Sistemas, que conserva todo lo que Jefatura puede hacer.
    /// </summary>
    public const string Jefatura = "Jefatura";

    public static void Registrar(AuthorizationOptions opciones)
    {
        opciones.AddPolicy(Estructura, p => p.RequireRole(ClaimsAnalista.RolSistemas));

        opciones.AddPolicy(Jefatura, p => p.RequireRole(ClaimsAnalista.RolSistemas, ClaimsAnalista.RolJefatura));
    }
}
