using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Infrastructure.Proveedores;

/// <summary>
/// Proveedor de desarrollo: registra el envio en el log y lo da por exitoso, sin salir a la red.
/// Permite trabajar sobre el flujo completo mientras 360dialog no tenga credenciales o el WABA
/// siga en aprobacion, que segun el dossier es el cuello de botella real del proyecto.
/// <para>
/// Usa el mismo interprete que el adaptador real, de modo que lo que se prueba en desarrollo sea
/// el codigo que corre en produccion.
/// </para>
/// </summary>
public sealed class ProveedorSimulado(
    TimeProvider reloj, ILogger<ProveedorSimulado> log, string? secretoWebhook = null) : IWhatsAppProvider
{
    /// <summary>Todo lo "enviado" queda aca para poder revisarlo desde las pruebas.</summary>
    public List<EnvioSimulado> Enviados { get; } = [];

    public string Nombre => "simulado";

    public Task<ResultadoEnvio> EnviarTextoAsync(string telefonoE164, string texto, CancellationToken ct = default)
    {
        log.LogInformation("[SIMULADO] Texto a {Telefono}: {Texto}", telefonoE164, texto);
        return Registrar(telefonoE164, "texto", texto);
    }

    public Task<ResultadoEnvio> EnviarPlantillaAsync(
        string telefonoE164, Plantilla plantilla, IReadOnlyList<string> parametros, CancellationToken ct = default)
    {
        // Misma guarda que en produccion: una plantilla sin aprobar no sale ni en desarrollo, para
        // no probar el flujo contra un camino que luego no va a existir.
        if (!plantilla.Activa)
        {
            return Task.FromResult(ResultadoEnvio.Permanente(
                $"La plantilla '{plantilla.Clave}' no esta activa."));
        }

        log.LogInformation("[SIMULADO] Plantilla {Clave} a {Telefono}", plantilla.Clave, telefonoE164);
        return Registrar(telefonoE164, "plantilla", plantilla.Clave);
    }

    public Task<ResultadoEnvio> EnviarBotonesAsync(
        string telefonoE164, string texto, IReadOnlyList<BotonRespuesta> botones, CancellationToken ct = default)
    {
        log.LogInformation("[SIMULADO] Botones a {Telefono}: {Opciones}",
            telefonoE164, string.Join(" | ", botones.Select(b => b.Titulo)));

        return Registrar(telefonoE164, "botones", string.Join(",", botones.Select(b => b.Id)));
    }

    public Task<ResultadoEnvio> EnviarListaAsync(
        string telefonoE164, string texto, string textoBoton,
        IReadOnlyList<BotonRespuesta> opciones, CancellationToken ct = default)
    {
        // Se respeta el mismo tope que en produccion: si el menu no cabe, tampoco cabe en desarrollo.
        if (opciones.Count is 0 || opciones.Count > CuerposMensaje.MaximoOpcionesLista)
        {
            return Task.FromResult(ResultadoEnvio.Permanente(
                $"WhatsApp admite entre 1 y {CuerposMensaje.MaximoOpcionesLista} opciones y se pidieron {opciones.Count}."));
        }

        log.LogInformation("[SIMULADO] Lista a {Telefono}: {Opciones}",
            telefonoE164, string.Join(" | ", opciones.Select(o => o.Titulo)));

        return Registrar(telefonoE164, "lista", string.Join(",", opciones.Select(o => o.Id)));
    }

    private Task<ResultadoEnvio> Registrar(string telefono, string tipo, string detalle)
    {
        Enviados.Add(new EnvioSimulado(telefono, tipo, detalle));
        return Task.FromResult(ResultadoEnvio.Ok($"simulado-{Guid.NewGuid():N}"));
    }

    /// <summary>
    /// Sin secreto configurado acepta todo, porque en desarrollo no hay un 360dialog real firmando.
    /// Con secreto, valida igual que en produccion: sirve para probar el rechazo de firmas malas.
    /// </summary>
    public bool ValidarFirma(string cuerpoCrudo, IReadOnlyDictionary<string, string> cabeceras)
    {
        if (string.IsNullOrWhiteSpace(secretoWebhook))
            return true;

        var recibida = cabeceras
            .FirstOrDefault(c => string.Equals(c.Key, Dialog360Provider.CabeceraFirma, StringComparison.OrdinalIgnoreCase))
            .Value;

        return !string.IsNullOrWhiteSpace(recibida)
            && Dialog360Provider.CalcularFirma(cuerpoCrudo, secretoWebhook)
                .Equals(recibida.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyList<MensajeEntranteDto> InterpretarWebhook(string cuerpoCrudo) =>
        InterpreteWebhookMeta.Mensajes(cuerpoCrudo, reloj.GetUtcNow().UtcDateTime, log);

    public IReadOnlyList<EstadoEntregaDto> InterpretarEstados(string cuerpoCrudo) =>
        InterpreteWebhookMeta.Estados(cuerpoCrudo, reloj.GetUtcNow().UtcDateTime, log);
}

public sealed record EnvioSimulado(string Telefono, string Tipo, string Detalle);
