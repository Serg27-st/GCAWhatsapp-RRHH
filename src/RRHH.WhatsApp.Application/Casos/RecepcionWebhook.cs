using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Application.Casos;

/// <summary>Tipos de evento que se publican en la outbox. Constantes para no repartir cadenas sueltas.</summary>
public static class TiposEvento
{
    public const string MensajeEntranteRecibido = "MensajeEntranteRecibido";
    public const string EnvioRequierePlantilla = "EnvioRequierePlantilla";

    /// <summary>La bandeja lo consume por SignalR; el evento sobrevive si el analista no esta conectado.</summary>
    public const string AnalistaNotificado = "AnalistaNotificado";

    /// <summary>Regla 12: una postulacion quedo descartada y corresponde el cierre de cortesia.</summary>
    public const string PostulacionDescartada = "PostulacionDescartada";

    /// <summary>Regla 9: el postulante completo el formulario. Lo consume el motor de reglas.</summary>
    public const string JobFormsCompletado = "JobFormsCompletado";

    /// <summary>Una vacante abierta no tiene formulario cargado: falta un dato de administracion.</summary>
    public const string VacanteSinFormulario = "VacanteSinFormulario";

    /// <summary>Una regla pidio una plantilla que no existe o que Meta todavia no aprobo.</summary>
    public const string EnvioOmitidoSinPlantilla = "EnvioOmitidoSinPlantilla";

    public const string MenuSinOpciones = "MenuSinOpciones";

    /// <summary>Hay mas cuentas con vacantes abiertas de las que WhatsApp deja mostrar en un menu.</summary>
    public const string MenuTruncado = "MenuTruncado";
}

public sealed record ResultadoRecepcion(
    bool FirmaValida,
    int MensajesNuevos,
    int MensajesDuplicados,
    int EstadosActualizados)
{
    public static readonly ResultadoRecepcion FirmaRechazada = new(false, 0, 0, 0);
}

/// <summary>
/// Gateway de mensajeria: recibe el webhook, valida la firma, normaliza y encola. No evalua reglas
/// ni envia nada.
/// <para>
/// La razon es concreta: 360dialog tiene un limite duro de 5 segundos para recibir un 200, y si no
/// lo recibe reintenta la entrega. Procesar las reglas aca haria que un pico de trafico se
/// convirtiera en reintentos, que es justo el patron de carga que conviene evitar.
/// </para>
/// </summary>
public sealed class RecepcionWebhook(
    IWhatsAppProvider proveedor,
    IConversacionService conversaciones,
    IMensajeService mensajes,
    IEventoSistemaService eventos,
    ILogger<RecepcionWebhook> log)
{
    public async Task<ResultadoRecepcion> ProcesarAsync(
        string cuerpoCrudo,
        IReadOnlyDictionary<string, string> cabeceras,
        CancellationToken ct = default)
    {
        // Primero la firma, antes de mirar el cuerpo. Un payload que no se puede verificar no se
        // procesa "por si acaso": se descarta.
        if (!proveedor.ValidarFirma(cuerpoCrudo, cabeceras))
            return ResultadoRecepcion.FirmaRechazada;

        var entrantes = proveedor.InterpretarWebhook(cuerpoCrudo);
        var acuses = proveedor.InterpretarEstados(cuerpoCrudo);

        var nuevos = 0;
        var duplicados = 0;

        foreach (var dto in entrantes)
        {
            var correlationId = Guid.NewGuid();

            var conversacion = await conversaciones.ObtenerOCrearAsync(dto.TelefonoE164, ct);

            var mensaje = await mensajes.RegistrarEntranteAsync(
                conversacion.ConversacionId, dto, correlationId, ct);

            // Nulo significa que Meta reintrego un mensaje que ya teniamos. No se vuelve a abrir
            // la ventana de 24h ni se vuelve a encolar: seria procesarlo dos veces.
            if (mensaje is null)
            {
                duplicados++;
                continue;
            }

            await conversaciones.RegistrarEntradaAsync(conversacion.ConversacionId, dto.FechaUtc, ct);

            await eventos.PublicarAsync(TiposEvento.MensajeEntranteRecibido, new
            {
                conversacion.ConversacionId,
                mensaje.MensajeId,
                dto.TelefonoE164,
                dto.Contenido,
                dto.IdBotonPulsado,
                dto.NombrePerfil,
                dto.FechaUtc
            }, correlationId, ct);

            nuevos++;
        }

        foreach (var acuse in acuses)
            await mensajes.ActualizarEstadoEntregaAsync(acuse, ct);

        if (nuevos > 0 || duplicados > 0 || acuses.Count > 0)
        {
            log.LogInformation(
                "Webhook procesado: {Nuevos} nuevos, {Duplicados} duplicados, {Acuses} acuses.",
                nuevos, duplicados, acuses.Count);
        }

        return new ResultadoRecepcion(true, nuevos, duplicados, acuses.Count);
    }
}
