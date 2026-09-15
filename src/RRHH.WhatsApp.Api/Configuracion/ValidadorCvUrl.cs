namespace RRHH.WhatsApp.Api.Configuracion;

/// <summary>
/// Valida el enlace del CV que llega del JobForms (COR-15/M8). El analista termina abriendo ese
/// enlace a ciegas —Google guarda el adjunto en Drive y lo que llega es su URL, no el archivo—, asi
/// que no alcanza con "es una URL": tiene que ser https y su host tiene que coincidir EXACTO con
/// uno de <see cref="OpcionesJobForms.DominiosCvPermitidos"/>. Coincidencia exacta y no
/// <c>EndsWith</c>/<c>Contains</c>: esas dos dejan pasar <c>drive.google.com.evil.com</c> o
/// <c>evil.com/drive.google.com</c>, que son intentos de imitar el dominio, no URLs legitimas.
/// </summary>
public static class ValidadorCvUrl
{
    /// <summary>
    /// El CV no es obligatorio aca (ver docs/decisiones.md sobre el limite de Google Forms sin
    /// login): un valor nulo o vacio se deja pasar. Si viene algo, tiene que ser una URL absoluta,
    /// https, con el host exacto en <paramref name="dominiosPermitidos"/>.
    /// </summary>
    public static bool EsValida(string? cvUrl, IReadOnlyCollection<string> dominiosPermitidos)
    {
        if (string.IsNullOrWhiteSpace(cvUrl))
            return true;

        if (!Uri.TryCreate(cvUrl, UriKind.Absolute, out var uri))
            return false;

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        // Uri ya resuelve https://usuario@evil.com al host real (evil.com): no hace falta mirar
        // UserInfo aparte, alcanza con comparar Host.
        foreach (var dominio in dominiosPermitidos)
        {
            if (string.Equals(uri.Host, dominio, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
