using System.Net;
using System.Net.Http.Json;
using RRHH.WhatsApp.Contracts.Bandeja;
using RRHH.WhatsApp.Contracts.Metricas;
using RRHH.WhatsApp.Contracts.Seguridad;

namespace RRHH.WhatsApp.Frontend.Servicios;

/// <summary>Lo que devuelve la Api cuando rechaza una accion por motivos de negocio.</summary>
public sealed record RespuestaApi(bool Exito, string? Motivo)
{
    public static readonly RespuestaApi Ok = new(true, null);
}

/// <summary>
/// Unico punto por el que el Frontend habla con la Api. Concentrarlo aca es lo que mantiene a las
/// pantallas sin saber de HTTP, y lo que hace que agregar autenticacion mas adelante sea tocar un
/// solo archivo.
/// </summary>
public sealed class ClienteApi(HttpClient http, SesionAnalista sesion, ILogger<ClienteApi> log)
{
    /// <summary>
    /// Inicia sesión. Nulo si las credenciales no sirven: el motivo exacto se queda en la Api a
    /// propósito, para no decirle a quien prueba contraseñas cuáles de los correos existen.
    /// </summary>
    public async Task<SesionIniciada?> LoginAsync(
        string email, string contrasena, CancellationToken ct = default)
    {
        try
        {
            var respuesta = await http.PostAsJsonAsync(
                "sesion/login", new PeticionLogin(email, contrasena), ct);

            return respuesta.IsSuccessStatusCode
                ? await respuesta.Content.ReadFromJsonAsync<SesionIniciada>(ct)
                : null;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Fallo el inicio de sesion.");

            return null;
        }
    }

    public Task<IReadOnlyList<AnalistaResumen>> AnalistasAsync(CancellationToken ct = default) =>
        LeerListaAsync<AnalistaResumen>("analistas", ct);

    public Task<IReadOnlyList<CuentaDeAnalista>> CuentasDeAnalistaAsync(int analistaId, CancellationToken ct = default) =>
        LeerListaAsync<CuentaDeAnalista>($"analistas/{analistaId}/cuentas", ct);

    public Task<IReadOnlyList<ConversacionResumen>> BandejaAsync(int analistaId, CancellationToken ct = default) =>
        LeerListaAsync<ConversacionResumen>($"conversaciones?analistaId={analistaId}", ct);

    /// <summary>Regla 19: la bandeja general que ve todo el mundo.</summary>
    public Task<IReadOnlyList<ConversacionResumen>> PendientesAsync(CancellationToken ct = default) =>
        LeerListaAsync<ConversacionResumen>("conversaciones/pendientes", ct);

    public Task<IReadOnlyList<ConversacionResumen>> BuscarPorDniAsync(string dni, CancellationToken ct = default) =>
        LeerListaAsync<ConversacionResumen>($"conversaciones/buscar?dni={Uri.EscapeDataString(dni)}", ct);

    public Task<ConversacionDetalle?> DetalleAsync(int conversacionId, CancellationToken ct = default) =>
        LeerAsync<ConversacionDetalle>($"conversaciones/{conversacionId}", ct);

    public Task<IReadOnlyList<PlantillaResumen>> PlantillasAsync(CancellationToken ct = default) =>
        LeerListaAsync<PlantillaResumen>("plantillas", ct);

    public Task<TableroKanban?> TableroAsync(int hcId, CancellationToken ct = default) =>
        LeerAsync<TableroKanban>($"hc/{hcId}/tablero", ct);

    public Task<MetricasGerencia?> MetricasAsync(DateTime desde, DateTime hasta, CancellationToken ct = default) =>
        LeerAsync<MetricasGerencia>(
            $"reportes/metricas?desde={desde:O}&hasta={hasta:O}", ct);

    /// <summary>
    /// Regla 15. Devuelve el resultado tal cual lo da la Api, incluido el rechazo: la bandeja
    /// necesita distinguir "no salio" de "no salio, elegi una plantilla".
    /// </summary>
    public async Task<ResultadoResponder> ResponderAsync(
        int conversacionId, PeticionResponder peticion, CancellationToken ct = default)
    {
        try
        {
            Autenticar();

            var respuesta = await http.PostAsJsonAsync($"conversaciones/{conversacionId}/responder", peticion, ct);

            // 200 y 422 traen los dos el mismo cuerpo: el 422 es un rechazo de negocio, no un error.
            if (respuesta.StatusCode is HttpStatusCode.OK or HttpStatusCode.UnprocessableEntity)
            {
                return await respuesta.Content.ReadFromJsonAsync<ResultadoResponder>(ct)
                    ?? new ResultadoResponder(false, "La Api no devolvio un resultado legible.", false, null);
            }

            return new ResultadoResponder(false, await MotivoAsync(respuesta, ct), false, null);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Fallo la respuesta en la conversacion {ConversacionId}.", conversacionId);

            return new ResultadoResponder(false, "No se pudo contactar a la Api.", false, null);
        }
    }

    public Task<RespuestaApi> MarcarAsync(int conversacionId, PeticionMarcar peticion, CancellationToken ct = default) =>
        EnviarAsync($"conversaciones/{conversacionId}/marcar", peticion, ct);

    public Task<RespuestaApi> TransferirAsync(
        int conversacionId, PeticionTransferir peticion, CancellationToken ct = default) =>
        EnviarAsync($"conversaciones/{conversacionId}/transferir", peticion, ct);

    public Task<RespuestaApi> MoverEtapaAsync(
        int conversacionId, PeticionMoverEtapa peticion, CancellationToken ct = default) =>
        EnviarAsync($"conversaciones/{conversacionId}/etapa", peticion, ct);

    private async Task<RespuestaApi> EnviarAsync<T>(string ruta, T cuerpo, CancellationToken ct)
    {
        try
        {
            Autenticar();

            var respuesta = await http.PostAsJsonAsync(ruta, cuerpo, ct);

            return respuesta.IsSuccessStatusCode
                ? RespuestaApi.Ok
                : new RespuestaApi(false, await MotivoAsync(respuesta, ct));
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Fallo el POST a {Ruta}.", ruta);

            return new RespuestaApi(false, "No se pudo contactar a la Api.");
        }
    }


    /// <summary>
    /// Pone el token en la petición. Se hace acá y no en cada llamada para que agregar un método
    /// nuevo no pueda olvidarse de autenticarlo.
    /// </summary>
    private void Autenticar()
    {
        http.DefaultRequestHeaders.Authorization = sesion.Token is { } token
            ? new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token)
            : null;
    }
    private async Task<T?> LeerAsync<T>(string ruta, CancellationToken ct)
    {
        try
        {
            Autenticar();

            return await http.GetFromJsonAsync<T>(ruta, ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Fallo el GET a {Ruta}.", ruta);

            return default;
        }
    }

    private async Task<IReadOnlyList<T>> LeerListaAsync<T>(string ruta, CancellationToken ct) =>
        await LeerAsync<List<T>>(ruta, ct) ?? [];

    /// <summary>La Api devuelve el motivo del rechazo en un campo suelto; se rescata para mostrarlo.</summary>
    private static async Task<string> MotivoAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        try
        {
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<Dictionary<string, object>>(ct);

            if (cuerpo?.TryGetValue("motivo", out var motivo) == true)
                return motivo.ToString() ?? respuesta.ReasonPhrase ?? "Rechazado.";
        }
        catch
        {
            // El cuerpo puede no ser JSON (un 500 crudo, por ejemplo). No vale la pena tratarlo
            // distinto: para el analista es lo mismo, la accion no se pudo hacer.
        }

        return respuesta.ReasonPhrase ?? $"La Api respondio {(int)respuesta.StatusCode}.";
    }
}
