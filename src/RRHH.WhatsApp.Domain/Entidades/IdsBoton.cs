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

    /// <summary>FUN-03: fila «Ver mas empresas» o «Volver al inicio» del menu paginado.</summary>
    public const string PrefijoPagina = "pag_";

    /// <summary>FUN-09: una de las postulaciones vivas que se ofrecen cuando hay mas de una cuenta.</summary>
    public const string PrefijoProceso = "proc_";

    /// <summary>
    /// FUN-09: el postulante no quiere seguir con ninguno de sus procesos y va al menu de empresas. Sin
    /// numero porque no apunta a nada: es la salida del menu de procesos.
    /// </summary>
    public const string OtraEmpresa = "otra_empresa";

    public static string ParaCuenta(int cuentaId) => $"{PrefijoCuenta}{cuentaId}";

    public static string ParaVacante(int hcId) => $"{PrefijoVacante}{hcId}";

    public static string ParaPagina(int pagina) => $"{PrefijoPagina}{pagina}";

    public static string ParaProceso(int postulacionId) => $"{PrefijoProceso}{postulacionId}";

    /// <summary>Devuelve el CuentaId si el boton corresponde al menu de empresas; si no, nulo.</summary>
    public static int? LeerCuenta(string? idBoton) => Leer(idBoton, PrefijoCuenta);

    public static int? LeerVacante(string? idBoton) => Leer(idBoton, PrefijoVacante);

    public static int? LeerPagina(string? idBoton) => Leer(idBoton, PrefijoPagina);

    /// <summary>Devuelve el PostulacionId si el boton corresponde al menu de procesos; si no, nulo.</summary>
    public static int? LeerProceso(string? idBoton) => Leer(idBoton, PrefijoProceso);

    public static bool EsOtraEmpresa(string? idBoton) => string.Equals(idBoton, OtraEmpresa, StringComparison.Ordinal);

    private static int? Leer(string? idBoton, string prefijo) =>
        idBoton is not null
        && idBoton.StartsWith(prefijo, StringComparison.Ordinal)
        && int.TryParse(idBoton[prefijo.Length..], out var id)
            ? id
            : null;
}
