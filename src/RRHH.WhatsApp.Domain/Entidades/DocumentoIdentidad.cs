namespace RRHH.WhatsApp.Domain.Entidades;

/// <summary>
/// Normalizacion del documento de identidad que llega desde el JobForms: DNI (8 digitos) o carne
/// de extranjeria (9 a 12 alfanumericos). Vive en Domain, sin dependencias, porque es el DNI el
/// que identifica a la persona (Regla 9) y un mismo documento guardado de una forma y buscado de
/// otra es, en la practica, alguien que el sistema no reconoce.
/// <para>
/// COR-15/M8: el borde publico del webhook recibia el DNI sin validar — un valor nulo tumbaba la
/// peticion con un 500 en vez de un rechazo del negocio.
/// </para>
/// </summary>
public static class DocumentoIdentidad
{
    /// <summary>
    /// Intenta normalizar <paramref name="valor"/> a un documento valido.
    /// <para>
    /// Se quitan espacios, guiones y puntos porque son separadores que la propia persona agrega
    /// al escribir el numero (por ejemplo "12.345.678" o "123-456-789"), no parte del documento.
    /// El resultado pasa a mayusculas antes de validarse.
    /// </para>
    /// </summary>
    /// <returns>
    /// <c>true</c> si, tras limpiarlo, el valor tiene exactamente 8 digitos (DNI) o entre 9 y 12
    /// caracteres alfanumericos (carne de extranjeria). Nulo, vacio o cualquier otra forma
    /// devuelve <c>false</c> con <paramref name="normalizado"/> vacio.
    /// </returns>
    public static bool TryNormalizar(string? valor, out string normalizado)
    {
        normalizado = string.Empty;

        if (string.IsNullOrWhiteSpace(valor))
            return false;

        var limpio = valor
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace(".", string.Empty)
            .ToUpperInvariant();

        if (limpio.Length == 8 && EsSoloDigitos(limpio))
        {
            normalizado = limpio;
            return true;
        }

        if (limpio.Length is >= 9 and <= 12 && EsAlfanumerico(limpio))
        {
            normalizado = limpio;
            return true;
        }

        return false;
    }

    private static bool EsSoloDigitos(string valor)
    {
        foreach (var c in valor)
        {
            if (c is < '0' or > '9')
                return false;
        }

        return true;
    }

    private static bool EsAlfanumerico(string valor)
    {
        foreach (var c in valor)
        {
            var esDigito = c is >= '0' and <= '9';
            var esLetra = c is >= 'A' and <= 'Z';

            if (!esDigito && !esLetra)
                return false;
        }

        return true;
    }
}
