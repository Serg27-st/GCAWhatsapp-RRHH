using System.Net;
using System.Net.Http.Json;
using RRHH.WhatsApp.Contracts.Administracion;
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
/// FUN-14: un archivo que sirve la Api, sin cargarlo entero en memoria. Quien lo recibe lo cierra:
/// detras queda la respuesta HTTP abierta.
/// </summary>
public sealed class ArchivoDeLaApi(HttpResponseMessage respuesta, Stream contenido) : IAsyncDisposable
{
    public Stream Contenido => contenido;

    /// <summary>El nombre con el que la Api lo manda a guardar.</summary>
    public string Nombre =>
        respuesta.Content.Headers.ContentDisposition?.FileNameStar
        ?? respuesta.Content.Headers.ContentDisposition?.FileName?.Trim('"')
        ?? "archivo";

    public async ValueTask DisposeAsync()
    {
        await contenido.DisposeAsync();
        respuesta.Dispose();
    }
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

    /// <summary>
    /// FUN-14: el archivo que mando el postulante. Lo pide el circuito y no el navegador, porque el
    /// token del analista vive solo aca: el navegador no lo lleva en una descarga comun.
    /// </summary>
    public async Task<ArchivoDeLaApi?> AdjuntoAsync(
        int conversacionId, long adjuntoId, CancellationToken ct = default)
    {
        HttpResponseMessage? respuesta = null;

        try
        {
            Autenticar();

            respuesta = await http.GetAsync(
                $"conversaciones/{conversacionId}/adjuntos/{adjuntoId}",
                HttpCompletionOption.ResponseHeadersRead,
                ct);

            if (!respuesta.IsSuccessStatusCode)
            {
                log.LogWarning("La Api no entrego el adjunto {AdjuntoId}: {Codigo}.",
                    adjuntoId, (int)respuesta.StatusCode);

                return null;
            }

            var archivo = new ArchivoDeLaApi(respuesta, await respuesta.Content.ReadAsStreamAsync(ct));

            // Desde aca la respuesta la cierra el archivo, cuando quien lo recibe termine de leerlo.
            respuesta = null;

            return archivo;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Fallo la descarga del adjunto {AdjuntoId}.", adjuntoId);

            return null;
        }
        finally
        {
            respuesta?.Dispose();
        }
    }

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

    /// <summary>
    /// FUN-01: el analista se adjudica un hilo de «Sin clasificar» para una de sus cuentas. Si otro lo
    /// tomo primero, la Api responde 409 y el motivo llega en <see cref="RespuestaApi.Motivo"/>.
    /// </summary>
    public Task<RespuestaApi> TomarAsync(int conversacionId, int cuentaId, CancellationToken ct = default) =>
        EnviarAsync($"conversaciones/{conversacionId}/tomar", new PeticionTomar(cuentaId), ct);

    public Task<RespuestaApi> TransferirAsync(
        int conversacionId, PeticionTransferir peticion, CancellationToken ct = default) =>
        EnviarAsync($"conversaciones/{conversacionId}/transferir", peticion, ct);

    /// <summary>Regla 8: lo que le transfirieron a quien está en sesión y espera su respuesta.</summary>
    public Task<IReadOnlyList<TransferenciaPendiente>> TransferenciasPendientesAsync(CancellationToken ct = default) =>
        LeerListaAsync<TransferenciaPendiente>("transferencias/pendientes", ct);

    public Task<RespuestaApi> ResponderTransferenciaAsync(
        int transferenciaId, bool aceptada, CancellationToken ct = default) =>
        EnviarAsync($"transferencias/{transferenciaId}/responder", new PeticionResponderTransferencia(aceptada), ct);

    /// <summary>FUN-07: lo que el analista ofrecio y sigue esperando respuesta.</summary>
    public Task<IReadOnlyList<TransferenciaEnviada>> TransferenciasEnviadasAsync(CancellationToken ct = default) =>
        LeerListaAsync<TransferenciaEnviada>("transferencias/enviadas", ct);

    public Task<RespuestaApi> RetirarTransferenciaAsync(int transferenciaId, CancellationToken ct = default) =>
        EnviarSinCuerpoAsync(HttpMethod.Post, $"transferencias/{transferenciaId}/retirar", ct);

    public Task<RespuestaApi> MoverEtapaAsync(
        int postulacionId, PeticionMoverEtapa peticion, CancellationToken ct = default) =>
        EnviarAsync($"postulaciones/{postulacionId}/etapa", peticion, ct);

    /// <summary>FUN-08 (A2): la persona vuelve a un proceso vivo y su tarjeta sale de la columna final.</summary>
    public Task<RespuestaApi> MarcarReingresoAsync(int postulacionId, CancellationToken ct = default) =>
        EnviarSinCuerpoAsync(HttpMethod.Post, $"postulaciones/{postulacionId}/reingreso", ct);

    // Administracion (V23). Quien puede que lo decide la Api; estos metodos solo traen su respuesta,
    // incluido el motivo cuando dice que no.

    public Task<RespuestaApi> CambiarContrasenaAsync(string actual, string nueva, CancellationToken ct = default) =>
        EnviarAsync(HttpMethod.Put, "sesion/contrasena", new PeticionCambiarContrasena(actual, nueva), ct);

    public Task<RespuestaApi> RestablecerContrasenaAsync(int analistaId, string nueva, CancellationToken ct = default) =>
        EnviarAsync(HttpMethod.Put, $"sesion/analistas/{analistaId}/contrasena", new PeticionRestablecerContrasena(nueva), ct);

    /// <summary>FUN-18: deja sin efecto los tokens que ese analista tenga abiertos, sin tocar su contraseña.</summary>
    public Task<RespuestaApi> CerrarSesionesAsync(int analistaId, CancellationToken ct = default) =>
        EnviarSinCuerpoAsync(HttpMethod.Post, $"sesion/analistas/{analistaId}/cerrar-sesiones", ct);

    public Task<IReadOnlyList<CuentaDetalle>> CuentasAsync(CancellationToken ct = default) =>
        LeerListaAsync<CuentaDetalle>("cuentas", ct);

    public Task<RespuestaApi> CrearCuentaAsync(string nombre, CancellationToken ct = default) =>
        EnviarAsync("cuentas", new PeticionCrearCuenta(nombre), ct);

    public Task<RespuestaApi> AsignarAnalistaAsync(int cuentaId, int analistaId, bool esBackup, CancellationToken ct = default) =>
        EnviarAsync($"cuentas/{cuentaId}/analistas", new PeticionAsignarAnalista(analistaId, esBackup), ct);

    public Task<RespuestaApi> QuitarAnalistaAsync(int cuentaId, int analistaId, CancellationToken ct = default) =>
        EnviarSinCuerpoAsync(HttpMethod.Delete, $"cuentas/{cuentaId}/analistas/{analistaId}", ct);

    public Task<RespuestaApi> CrearAnalistaAsync(PeticionCrearAnalista peticion, CancellationToken ct = default) =>
        EnviarAsync("analistas", peticion, ct);

    /// <summary>FUN-19: lo que ese analista tiene encima, para mostrarlo antes de darlo de baja.</summary>
    public Task<CarteraAnalista?> CarteraAsync(int analistaId, CancellationToken ct = default) =>
        LeerAsync<CarteraAnalista>($"analistas/{analistaId}/cartera", ct);

    /// <summary>FUN-19: lo que viene nulo no se toca; se manda solo lo que cambia.</summary>
    public Task<RespuestaApi> EditarAnalistaAsync(
        int analistaId, PeticionEditarAnalista peticion, CancellationToken ct = default) =>
        EnviarAsync(HttpMethod.Patch, $"analistas/{analistaId}", peticion, ct);

    public Task<IReadOnlyList<AusenciaResumen>> AusenciasAsync(int analistaId, CancellationToken ct = default) =>
        LeerListaAsync<AusenciaResumen>($"analistas/{analistaId}/ausencias", ct);

    public Task<RespuestaApi> RegistrarAusenciaAsync(int analistaId, PeticionAusencia peticion, CancellationToken ct = default) =>
        EnviarAsync($"analistas/{analistaId}/ausencias", peticion, ct);

    public Task<RespuestaApi> CancelarAusenciaAsync(int analistaId, int ausenciaId, CancellationToken ct = default) =>
        EnviarSinCuerpoAsync(HttpMethod.Delete, $"analistas/{analistaId}/ausencias/{ausenciaId}", ct);

    public Task<IReadOnlyList<VacanteResumen>> VacantesAsync(
        int cuentaId, bool incluirCerradas = false, CancellationToken ct = default) =>
        LeerListaAsync<VacanteResumen>($"hc?cuentaId={cuentaId}&incluirCerradas={incluirCerradas}", ct);

    public Task<RespuestaApi> CrearVacanteAsync(PeticionCrearVacante peticion, CancellationToken ct = default) =>
        EnviarAsync("hc", peticion, ct);

    public Task<RespuestaApi> CerrarVacanteAsync(int hcId, CancellationToken ct = default) =>
        EnviarSinCuerpoAsync(HttpMethod.Patch, $"hc/{hcId}/cerrar", ct);

    /// <summary>FUN-20: corregir título, enlace del formulario o código de aviso. Lo nulo no se toca.</summary>
    public Task<RespuestaApi> EditarVacanteAsync(
        int hcId, PeticionEditarVacante peticion, CancellationToken ct = default) =>
        EnviarAsync(HttpMethod.Patch, $"hc/{hcId}", peticion, ct);

    public Task<RespuestaApi> ReabrirVacanteAsync(int hcId, CancellationToken ct = default) =>
        EnviarSinCuerpoAsync(HttpMethod.Patch, $"hc/{hcId}/reabrir", ct);

    /// <summary>FUN-20: corregir el nombre de una cuenta o desactivarla.</summary>
    public Task<RespuestaApi> EditarCuentaAsync(
        int cuentaId, PeticionEditarCuenta peticion, CancellationToken ct = default) =>
        EnviarAsync(HttpMethod.Patch, $"cuentas/{cuentaId}", peticion, ct);

    /// <summary>FUN-02: el codigo del aviso de la vacante y el enlace de WhatsApp que lo lleva escrito.</summary>
    public Task<EnlaceAviso?> EnlaceAvisoAsync(int hcId, CancellationToken ct = default) =>
        LeerAsync<EnlaceAviso>($"hc/{hcId}/enlace-aviso", ct);

    public Task<IReadOnlyList<CampoOpcional>> CamposAsync(int hcId, CancellationToken ct = default) =>
        LeerListaAsync<CampoOpcional>($"hc/{hcId}/campos", ct);

    public Task<RespuestaApi> GuardarCamposAsync(int hcId, IReadOnlyList<CampoOpcional> campos, CancellationToken ct = default) =>
        EnviarAsync(HttpMethod.Put, $"hc/{hcId}/campos", campos, ct);

    public Task<HorarioVigente?> HorarioAsync(int? cuentaId, CancellationToken ct = default) =>
        LeerAsync<HorarioVigente>(RutaHorario(cuentaId), ct);

    public Task<RespuestaApi> GuardarHorarioAsync(int? cuentaId, IReadOnlyList<TramoHorario> tramos, CancellationToken ct = default) =>
        EnviarAsync(HttpMethod.Put, RutaHorario(cuentaId), tramos, ct);

    /// <summary>FUN-15: lo que una persona tiene que arreglar, agrupado y con su contador.</summary>
    public Task<IReadOnlyList<AlertaOperativaResumen>> AlertasAsync(CancellationToken ct = default) =>
        LeerListaAsync<AlertaOperativaResumen>("operacion/alertas", ct);

    public Task<RespuestaApi> ResolverAlertaAsync(int alertaId, CancellationToken ct = default) =>
        EnviarSinCuerpoAsync(HttpMethod.Post, $"operacion/alertas/{alertaId}/resolver", ct);

    public Task<IReadOnlyList<ParametroRegla>> ParametrosAsync(CancellationToken ct = default) =>
        LeerListaAsync<ParametroRegla>("configuracion/reglas", ct);

    /// <summary>El valor viaja como cadena JSON: es lo que espera la Api, que lo valida contra el tipo actual.</summary>
    public Task<RespuestaApi> GuardarParametroAsync(string clave, string valor, CancellationToken ct = default) =>
        EnviarAsync(HttpMethod.Put, $"configuracion/reglas/{Uri.EscapeDataString(clave)}", valor, ct);

    private static string RutaHorario(int? cuentaId) =>
        cuentaId is { } id ? $"configuracion/horario?cuentaId={id}" : "configuracion/horario";

    private Task<RespuestaApi> EnviarAsync<T>(string ruta, T cuerpo, CancellationToken ct) =>
        EnviarAsync(HttpMethod.Post, ruta, cuerpo, ct);

    private Task<RespuestaApi> EnviarSinCuerpoAsync(HttpMethod metodo, string ruta, CancellationToken ct) =>
        EnviarAsync<object?>(metodo, ruta, null, ct);

    private async Task<RespuestaApi> EnviarAsync<T>(HttpMethod metodo, string ruta, T cuerpo, CancellationToken ct)
    {
        try
        {
            Autenticar();

            using var peticion = new HttpRequestMessage(metodo, ruta)
            {
                Content = cuerpo is null ? null : JsonContent.Create(cuerpo)
            };

            var respuesta = await http.SendAsync(peticion, ct);

            return respuesta.IsSuccessStatusCode
                ? RespuestaApi.Ok
                : new RespuestaApi(false, await MotivoAsync(respuesta, ct));
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Fallo el {Metodo} a {Ruta}.", metodo, ruta);

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
        // Sin sesion o con otro rol la Api no manda motivo. Se traduce aca para que la pantalla no
        // muestre "Forbidden" a quien no sabe que es.
        if (respuesta.StatusCode == HttpStatusCode.Forbidden)
            return await LeerMotivoAsync(respuesta, ct) ?? "Tu rol no permite hacer esto.";

        if (respuesta.StatusCode == HttpStatusCode.Unauthorized)
            return "Tu sesión expiró. Volvé a entrar.";

        return await LeerMotivoAsync(respuesta, ct)
            ?? respuesta.ReasonPhrase
            ?? $"La Api respondio {(int)respuesta.StatusCode}.";
    }

    private static async Task<string?> LeerMotivoAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        try
        {
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<Dictionary<string, object>>(ct);

            if (cuerpo?.TryGetValue("motivo", out var motivo) == true)
                return motivo.ToString();
        }
        catch
        {
            // El cuerpo puede no ser JSON (un 500 crudo, o un 403 sin cuerpo). No vale la pena
            // tratarlo distinto: para el analista es lo mismo, la accion no se pudo hacer.
        }

        return null;
    }
}
