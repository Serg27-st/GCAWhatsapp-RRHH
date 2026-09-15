namespace RRHH.WhatsApp.Infrastructure.Proveedores;

/// <summary>
/// Clasifica una falla de red como Transitoria o Ambigua para <see cref="MetaCloudProvider"/> y
/// <see cref="Dialog360Provider"/> (T0.03, hallazgo C4, especificación ARQ-04).
/// <para>
/// Hasta acá el <c>AddStandardResilienceHandler()</c> de <c>RegistroDependencias</c> reintentaba
/// el POST por su cuenta ante 5xx, 408, 429, <see cref="HttpRequestException"/> y timeout —anulaba
/// el diseño de "Ambiguo no se reintenta solo", saltaba el <see cref="LimitadorEnvio"/> porque cada
/// reintento interno esperaba turno una sola vez por envío lógico, y se sumaba a los reintentos de
/// <c>ReintentoEnvios</c>. Es el patrón de mensajes duplicados que costó la línea (Sección 9.6.4,
/// decisión V29 de <c>docs/decisiones.md</c>). Quitado el handler, cada adaptador tiene que hacer
/// esta clasificación él mismo, porque ahora es la única que hay.
/// </para>
/// </summary>
internal static class ClasificadorFallosHttp
{
    /// <summary>
    /// True solo cuando la petición nunca llegó a salir por la red: no hubo resolución de DNS, no
    /// hubo conexión TCP o falló el handshake TLS. En esos tres casos Meta (directo o vía
    /// 360dialog) nunca vio el mensaje, así que reintentarlo es seguro (Transitorio).
    /// <para>
    /// Cualquier otro <see cref="HttpRequestException"/> —la conexión se cortó a mitad de
    /// respuesta, el protocolo vino roto, etc.— pudo ocurrir después de que la petición viajó: ahí
    /// no se sabe si Meta llegó a procesarla, y reenviarla podría duplicar el mensaje (Ambiguo).
    /// <c>ReintentoEnvios</c> solo toma los Transitorios, así que un Ambiguo nunca sale dos veces
    /// solo.
    /// </para>
    /// </summary>
    public static bool NuncaLlegoASalir(HttpRequestException ex) =>
        ex.HttpRequestError is HttpRequestError.NameResolutionError
            or HttpRequestError.ConnectionError
            or HttpRequestError.SecureConnectionError;
}
