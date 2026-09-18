using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Tests.Proveedores;

/// <summary>
/// Proveedor que responde lo que la prueba le indique —un 503, un rechazo, un acuse perdido— y
/// cuenta las llamadas. El simulado siempre responde bien, y lo que se prueba acá es qué pasa cuando no.
/// </summary>
internal sealed class ProveedorFalso : IWhatsAppProvider
{
    public ResultadoEnvio Respuesta { get; set; } = ResultadoEnvio.Ok("id");

    public int Llamadas => Envios.Count;

    /// <summary>Tipo de cada llamada: texto, plantilla, botones o lista.</summary>
    public List<string> Envios { get; } = [];

    public string Nombre => "falso";

    public Task<ResultadoEnvio> EnviarTextoAsync(string t, string x, CancellationToken ct = default) =>
        Registrar("texto");

    public Task<ResultadoEnvio> EnviarPlantillaAsync(
        string t, Plantilla p, IReadOnlyList<string> par, CancellationToken ct = default) =>
        Registrar("plantilla");

    public Task<ResultadoEnvio> EnviarBotonesAsync(
        string t, string x, IReadOnlyList<BotonRespuesta> b, CancellationToken ct = default) =>
        Registrar("botones");

    public Task<ResultadoEnvio> EnviarListaAsync(
        string t, string x, string b, IReadOnlyList<BotonRespuesta> o, CancellationToken ct = default) =>
        Registrar("lista");

    public bool ValidarFirma(string c, IReadOnlyDictionary<string, string> h) => true;

    public IReadOnlyList<MensajeEntranteDto> InterpretarWebhook(string c) => [];

    public IReadOnlyList<EstadoEntregaDto> InterpretarEstados(string c) => [];

    /// <summary>
    /// Lo que hace cada descarga. Una función, para que cada llamada reciba su propio flujo y para
    /// poder simular una que no termina nunca.
    /// </summary>
    public Func<string, CancellationToken, Task<ResultadoDescarga>> Descarga { get; set; } =
        (_, _) => Task.FromResult(ResultadoDescarga.Permanente("El proveedor falso no tiene archivos."));

    /// <summary>Ids pedidos, en orden.</summary>
    public List<string> Descargas { get; } = [];

    public Task<ResultadoDescarga> DescargarMedioAsync(string id, CancellationToken ct = default)
    {
        Descargas.Add(id);
        return Descarga(id, ct);
    }

    private Task<ResultadoEnvio> Registrar(string tipo)
    {
        Envios.Add(tipo);
        return Task.FromResult(Respuesta);
    }
}
