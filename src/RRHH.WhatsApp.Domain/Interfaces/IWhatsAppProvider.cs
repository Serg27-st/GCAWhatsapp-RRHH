using RRHH.WhatsApp.Domain.Entidades;

namespace RRHH.WhatsApp.Domain.Interfaces;

/// <summary>Mensaje entrante ya normalizado, independiente del proveedor que lo entrego.</summary>
public sealed record MensajeEntranteDto(
    string ProviderMessageId,
    string TelefonoE164,
    string? NombrePerfil,
    string Contenido,
    string? IdBotonPulsado,
    DateTime FechaUtc);

public sealed record ResultadoEnvio(bool Exito, string? ProviderMessageId, string? Error);

/// <summary>Boton del menu de empresas que arma el bot (Regla 19: botones, no texto libre).</summary>
public sealed record BotonRespuesta(string Id, string Titulo);

/// <summary>
/// Patron Adapter: interfaz unica hacia el proveedor de WhatsApp. Cambiar de 360dialog a Meta
/// Cloud API implica reemplazar una implementacion, no reescribir el sistema.
/// <para>
/// La implementacion es tambien la responsable de limitar la velocidad de envio saliente
/// (Seccion 9.6.4), para no repetir el patron de uso que causo el bloqueo original.
/// </para>
/// </summary>
public interface IWhatsAppProvider
{
    string Nombre { get; }

    Task<ResultadoEnvio> EnviarTextoAsync(string telefonoE164, string texto, CancellationToken ct = default);

    Task<ResultadoEnvio> EnviarPlantillaAsync(
        string telefonoE164,
        Plantilla plantilla,
        IReadOnlyList<string> parametros,
        CancellationToken ct = default);

    Task<ResultadoEnvio> EnviarBotonesAsync(
        string telefonoE164,
        string texto,
        IReadOnlyList<BotonRespuesta> botones,
        CancellationToken ct = default);

    /// <summary>Valida la firma de la peticion antes de procesar nada. El Gateway no confia en el cuerpo sin esto.</summary>
    bool ValidarFirma(string cuerpoCrudo, IReadOnlyDictionary<string, string> cabeceras);

    /// <summary>Traduce el payload propio del proveedor a la forma normalizada del dominio.</summary>
    IReadOnlyList<MensajeEntranteDto> InterpretarWebhook(string cuerpoCrudo);
}
