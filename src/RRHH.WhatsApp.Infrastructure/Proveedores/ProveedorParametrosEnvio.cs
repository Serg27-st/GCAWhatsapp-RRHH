using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Infrastructure.Proveedores;

/// <summary>
/// Resuelve <c>envio.maximo_por_segundo</c> para <see cref="LimitadorEnvio"/> (COR-12/AL8,
/// Seccion 9.6.4: el volumen saliente sin control es una de las causas del bloqueo original).
/// <para>
/// Es singleton, pero <see cref="IConfiguracionReglasService"/> es scoped porque usa el
/// <c>DbContext</c>: por eso este servicio recibe un <see cref="IServiceScopeFactory"/> en vez
/// del servicio directo, y abre su propio ambito para leer. Inyectarlo directo seria una
/// dependencia cautiva (un scoped viviendo dentro de un singleton) y
/// <c>RegistroDependenciasTests</c> lo detectaria con <c>ValidateScopes = true</c>.
/// </para>
/// <para>
/// El valor se cachea 30 segundos. Es una constante tecnica, no un tope de negocio —por eso no
/// vive en <c>ConfiguracionReglas</c> como los demas parametros—: acota cuanto puede tardar un
/// cambio de <c>envio.maximo_por_segundo</c> en llegar al limitador sin redeploy, y evita abrir
/// un ambito y golpear la base en cada mensaje saliente. <see cref="ConfiguracionReglasService"/>
/// ya cachea <c>ObtenerTodasAsync</c> con la misma ventana de 30 s para el motor de reglas; esta
/// cache no la reemplaza (vive en un objeto distinto, con su propio reloj) sino que evita que
/// cada turno del limitador tenga que abrir un ambito y pasar por ella, que en el pico de
/// ~16,000 interacciones/mes puede ser varias veces por segundo.
/// </para>
/// <para>
/// Una falla leyendo el parametro (la base caida) nunca debe frenar un envio: se conserva el
/// ultimo valor conocido —o el fallback de appsettings si todavia no hubo ninguno— y se registra
/// un warning. Tampoco se reintenta la base en cada mensaje mientras esta caida: la cache se
/// renueva igual, asi que el siguiente intento espera los 30 s completos antes de volver a
/// golpearla.
/// </para>
/// </summary>
public sealed class ProveedorParametrosEnvio
{
    // Constante tecnica (por eso no sale de ConfiguracionReglas): ver el comentario de la clase.
    private static readonly TimeSpan Vigencia = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _fabricaAmbitos;
    private readonly int _valorFallback;
    private readonly TimeProvider _reloj;
    private readonly ILogger<ProveedorParametrosEnvio> _log;

    private readonly SemaphoreSlim _puerta = new(1, 1);
    private int _valorActual;
    private DateTimeOffset _proximaRecarga = DateTimeOffset.MinValue;
    private bool _cargadoAlgunaVez;

    public ProveedorParametrosEnvio(
        IServiceScopeFactory fabricaAmbitos,
        int valorFallback,
        TimeProvider reloj,
        ILogger<ProveedorParametrosEnvio> log)
    {
        _fabricaAmbitos = fabricaAmbitos;
        _valorFallback = valorFallback;
        _reloj = reloj;
        _log = log;
        _valorActual = valorFallback;
    }

    public async ValueTask<int> ObtenerMaximoPorSegundoAsync(CancellationToken ct)
    {
        if (_reloj.GetUtcNow() < _proximaRecarga)
            return _valorActual;

        // Protege la recarga: sin esto, dos envios concurrentes con la cache recien vencida
        // dispararian dos lecturas a la base a la vez en vez de una.
        await _puerta.WaitAsync(ct);
        try
        {
            // Otro llamador pudo haber recargado mientras se esperaba la puerta.
            if (_reloj.GetUtcNow() < _proximaRecarga)
                return _valorActual;

            await RecargarAsync(ct);
            return _valorActual;
        }
        finally
        {
            _puerta.Release();
        }
    }

    private async Task RecargarAsync(CancellationToken ct)
    {
        // La vigencia se renueva antes de leer, no despues: si la base esta caida, cada mensaje
        // volveria a intentar la conexion en vez de esperar los 30 s completos hasta el proximo
        // intento.
        _proximaRecarga = _reloj.GetUtcNow() + Vigencia;

        try
        {
            using var ambito = _fabricaAmbitos.CreateScope();
            var configuracion = ambito.ServiceProvider.GetRequiredService<IConfiguracionReglasService>();
            var valores = await configuracion.ObtenerTodasAsync(ct);

            if (valores.TryGetValue(ClavesConfiguracion.EnvioMaximoPorSegundo, out var texto) &&
                int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var valor) &&
                valor > 0)
            {
                _valorActual = valor;
            }
            else
            {
                // Clave ausente, no numerica o <= 0: mismo respaldo que si la base no respondiera.
                _log.LogWarning(
                    "envio.maximo_por_segundo ausente o invalido ('{Valor}'); se usa el fallback de appsettings ({Fallback})",
                    texto, _valorFallback);
                _valorActual = _valorFallback;
            }

            _cargadoAlgunaVez = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // AL8/COR-12: la velocidad de envio no puede depender de que la base este arriba en
            // este instante. Se sigue con el ultimo valor conocido (el fallback si esta es la
            // primera lectura) y se avisa; la cache ya quedo renovada arriba, asi que el proximo
            // mensaje no vuelve a golpear una base caida antes de los 30 s.
            _log.LogWarning(ex,
                "No se pudo leer envio.maximo_por_segundo; se mantiene el ultimo valor conocido ({Valor})",
                _cargadoAlgunaVez ? _valorActual : _valorFallback);

            if (!_cargadoAlgunaVez)
                _valorActual = _valorFallback;
        }
    }
}
