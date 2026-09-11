namespace RRHH.WhatsApp.Domain.Entidades;

/// <summary>
/// Convencion de los identificadores que viajan en los botones del bot. WhatsApp devuelve el id
/// tal cual se envio, asi que es el unico dato fiable para saber que eligio el postulante: el
/// titulo puede repetirse entre cuentas y el texto libre no sirve (Regla 19).
/// </summary>
public static class IdsBoton
{
    public const string PrefijoCuenta = "cuenta_";
    public const string PrefijoVacante = "hc_";

    public static string ParaCuenta(int cuentaId) => $"{PrefijoCuenta}{cuentaId}";

    public static string ParaVacante(int hcId) => $"{PrefijoVacante}{hcId}";

    /// <summary>Devuelve el CuentaId si el boton corresponde al menu de empresas; si no, nulo.</summary>
    public static int? LeerCuenta(string? idBoton) => Leer(idBoton, PrefijoCuenta);

    public static int? LeerVacante(string? idBoton) => Leer(idBoton, PrefijoVacante);

    private static int? Leer(string? idBoton, string prefijo) =>
        idBoton is not null
        && idBoton.StartsWith(prefijo, StringComparison.Ordinal)
        && int.TryParse(idBoton[prefijo.Length..], out var id)
            ? id
            : null;
}
