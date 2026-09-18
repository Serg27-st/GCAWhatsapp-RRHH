using System.Text.Json;
using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Application.Casos;

public sealed record ResumenDespacho(int Recuperados, int Intentados, int Logrados, int Fallidos)
{
    public static readonly ResumenDespacho Vacio = new(0, 0, 0, 0);

    public override string ToString() =>
        $"{Intentados} envio(s): {Logrados} logrado(s), {Fallidos} fallido(s); {Recuperados} Enviando recuperado(s).";
}

/// <summary>
/// Envia lo que las reglas encolaron (V29, ARQ-03).
/// <para>
/// Decidir y enviar quedaron separados porque un envio no se deshace: si algo fallaba despues de
/// llamar al proveedor, el reproceso del evento volvia a mandar menus y confirmaciones (C5). Ahora
/// procesar un evento solo escribe filas con clave unica, y esto es lo unico que habla con Meta en
/// nombre del bot. Pasa por el limitador del adaptador, igual que todo lo demas.
/// </para>
/// </summary>
public sealed class DespachoEnvios(
    IMensajeService mensajes,
    IConversacionService conversaciones,
    IPlantillaService plantillas,
    IWhatsAppProvider proveedor,
    ValidadorEnvio validador,
    IConfiguracionReglasService configuracion,
    TimeProvider reloj,
    ILogger<DespachoEnvios> log)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <param name="timeoutEnviando">
    /// Cuanto puede quedar un mensaje Enviando antes de darlo por perdido. Mayor que el timeout HTTP
    /// del adaptador, para no marcar ambiguo un envio que todavia esta en curso.
    /// </param>
    public async Task<ResumenDespacho> ProcesarAsync(int maximo, TimeSpan timeoutEnviando, CancellationToken ct = default)
    {
        // Primero lo atascado: si el proceso murio a mitad de un envio, esas filas no vuelven a la
        // cola (pudieron haber salido) y tienen que quedar visibles como ambiguas.
        var recuperados = await mensajes.RecuperarEnviandoVencidosAsync(timeoutEnviando, ct);

        var lote = await mensajes.TomarLoteEnColaAsync(maximo, ct);

        if (lote.Count == 0)
            return recuperados == 0 ? ResumenDespacho.Vacio : new(recuperados, 0, 0, 0);

        var config = await configuracion.ObtenerTodasAsync(ct);
        var baseSegundos = config.TryGetValue(ClavesConfiguracion.EnvioReintentoBaseSegundos, out var valor)
            && int.TryParse(valor, out var segundos) ? segundos : 60;

        var logrados = 0;
        var fallidos = 0;

        foreach (var mensaje in lote)
        {
            ct.ThrowIfCancellationRequested();

            if (await validador.MotivoParaNoEnviarAsync(mensaje, ct) is { } motivo)
            {
                log.LogWarning("El mensaje encolado {MensajeId} no sale: {Motivo}", mensaje.MensajeId, motivo);

                await mensajes.MarcarEnvioFallidoAsync(mensaje.MensajeId, motivo, ClaseFallo.Permanente, null, ct);
                fallidos++;
                continue;
            }

            var resultado = await EnviarSegunTipoAsync(mensaje, ct);

            if (resultado.Exito)
            {
                await mensajes.MarcarEnvioLogradoAsync(mensaje.MensajeId, resultado.ProviderMessageId, ct);
                logrados++;
                continue;
            }

            // Solo lo transitorio se agenda: lo permanente no cambia reintentando y lo ambiguo pudo
            // haber salido. El reintento lo toma ReintentoEnvios, el unico del sistema (ARQ-04).
            var proximo = resultado.Clase == ClaseFallo.Transitorio
                ? reloj.GetUtcNow().UtcDateTime.AddSeconds(baseSegundos)
                : (DateTime?)null;

            log.LogError("Fallo el envio del mensaje {MensajeId} ({Clase}): {Error}",
                mensaje.MensajeId, resultado.Clase, resultado.Error);

            await mensajes.MarcarEnvioFallidoAsync(mensaje.MensajeId, resultado.Error, resultado.Clase, proximo, ct);
            fallidos++;
        }

        return new ResumenDespacho(recuperados, lote.Count, logrados, fallidos);
    }

    /// <summary>
    /// Arma la llamada al proveedor con lo que quedo guardado en la fila. Lo usan tambien el reintento
    /// y la respuesta del analista, para que los tres manden un mensaje de la misma manera.
    /// </summary>
    public async Task<ResultadoEnvio> EnviarSegunTipoAsync(Mensaje mensaje, CancellationToken ct = default)
    {
        var telefono = mensaje.Conversacion?.TelefonoE164
            ?? (await conversaciones.ObtenerPorIdAsync(mensaje.ConversacionId, ct))?.TelefonoE164;

        if (telefono is null)
            return ResultadoEnvio.Permanente("La conversacion ya no existe.");

        if (ValidadorEnvio.EsPlantilla(mensaje))
        {
            var plantilla = mensaje.PlantillaId is { } id ? await plantillas.ObtenerPorIdAsync(id, ct) : null;

            if (plantilla is null || !plantilla.Activa)
                return ResultadoEnvio.Permanente("La plantilla no esta activa o aprobada en Meta.");

            return await proveedor.EnviarPlantillaAsync(telefono, plantilla, LeerParametros(mensaje), ct);
        }

        switch (mensaje.TipoSaliente)
        {
            case TipoSaliente.Botones when LeerOpciones(mensaje) is { } botones:
                return await proveedor.EnviarBotonesAsync(telefono, mensaje.Contenido, botones.Opciones, ct);

            case TipoSaliente.Lista when LeerOpciones(mensaje) is { } lista:
                return await proveedor.EnviarListaAsync(
                    telefono, mensaje.Contenido, lista.TextoBotonLista ?? "Ver opciones", lista.Opciones, ct);

            case TipoSaliente.Botones or TipoSaliente.Lista:
                // Un menu sin opciones no se puede rearmar; mandarlo como texto le mostraria al
                // postulante un "elige una opcion" sin nada que elegir.
                return ResultadoEnvio.Permanente("El menu encolado no tiene opciones para rearmarlo.");

            default:
                return await proveedor.EnviarTextoAsync(telefono, mensaje.Contenido, ct);
        }
    }

    private IReadOnlyList<string> LeerParametros(Mensaje mensaje)
    {
        if (string.IsNullOrWhiteSpace(mensaje.ParametrosPlantillaJson))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<string>>(mensaje.ParametrosPlantillaJson) ?? [];
        }
        catch (JsonException ex)
        {
            log.LogError(ex, "No se pudieron leer los parametros del mensaje {MensajeId}.", mensaje.MensajeId);
            return [];
        }
    }

    private OpcionesSaliente? LeerOpciones(Mensaje mensaje)
    {
        if (string.IsNullOrWhiteSpace(mensaje.OpcionesJson))
            return null;

        try
        {
            var opciones = JsonSerializer.Deserialize<OpcionesSaliente>(mensaje.OpcionesJson, Json);
            return opciones is { Opciones.Count: > 0 } ? opciones : null;
        }
        catch (JsonException ex)
        {
            log.LogError(ex, "No se pudieron leer las opciones del mensaje {MensajeId}.", mensaje.MensajeId);
            return null;
        }
    }
}
