using System.Diagnostics;

namespace RRHH.WhatsApp.Infrastructure.Proveedores;

/// <summary>
/// Limita cuantos mensajes salen por segundo hacia el proveedor de WhatsApp (Seccion 9.6.4).
/// <para>
/// No es una optimizacion: el volumen saliente sin control es una de las tres causas del bloqueo
/// original descritas en la Seccion 2.4. Ventana deslizante de un segundo sobre los ultimos
/// envios; cuando se llena, el llamador espera en vez de descartarse, porque perder un mensaje
/// de RRHH es peor que enviarlo unos milisegundos despues.
/// </para>
/// <para>
/// El tope se aplica por proceso emisor, no globalmente entre procesos: con ARQ-03 el unico
/// emisor del bot pasa a ser el Worker (la Api solo despacha las respuestas humanas de los
/// analistas), asi que un <see cref="LimitadorEnvio"/> singleton por proceso alcanza. Coordinar
/// el tope entre dos procesos que emitieran el mismo bot a la vez necesitaria un contador
/// compartido (por ejemplo en la base); eso no es el diseño actual.
/// </para>
/// </summary>
public sealed class LimitadorEnvio : IDisposable
{
    private static readonly long TicksPorVentana = Stopwatch.Frequency;

    private readonly Func<CancellationToken, ValueTask<int>> _maximoActual;
    private readonly SemaphoreSlim _puerta = new(1, 1);
    private readonly Queue<long> _envios = new();

    /// <summary>
    /// Constructor principal. COR-12/AL8 (03 §COR-12) pedia <c>Func&lt;int&gt;</c>, pero
    /// <c>envio.maximo_por_segundo</c> vive en la base y leerlo es asincrono (abre un ambito y
    /// consulta a traves de <see cref="ProveedorParametrosEnvio"/>). Este es el desvio
    /// intencional: <c>Func&lt;CancellationToken, ValueTask&lt;int&gt;&gt;</c> evita el
    /// sync-over-async de bloquear el hilo esperando esa lectura. Con el valor ya en cache —el
    /// caso comun, porque <see cref="ProveedorParametrosEnvio"/> cachea 30 s— la ruta sigue
    /// siendo sincronica y sin reservar memoria, porque el <see cref="ValueTask{TResult}"/> ya
    /// llega completado.
    /// </summary>
    public LimitadorEnvio(Func<CancellationToken, ValueTask<int>> maximoActual)
    {
        _maximoActual = maximoActual;
    }

    /// <summary>Conveniencia para pruebas y el proveedor simulado: un tope fijo que no cambia.</summary>
    public LimitadorEnvio(int maximoPorSegundo)
        : this(_ => ValueTask.FromResult(maximoPorSegundo > 0 ? maximoPorSegundo : 1))
    {
    }

    public async Task EsperarTurnoAsync(CancellationToken ct = default)
    {
        await _puerta.WaitAsync(ct);
        try
        {
            while (true)
            {
                // Se relee en cada vuelta: si el tope bajo mientras se esperaba (por ejemplo
                // porque expiro la cache de ProveedorParametrosEnvio entre un intento y el
                // siguiente), la comparacion de abajo ya usa el valor nuevo.
                var maximo = await _maximoActual(ct);
                if (maximo <= 0)
                    maximo = 1;

                var ahora = Stopwatch.GetTimestamp();

                // Descarta los envios que ya salieron de la ventana de un segundo.
                while (_envios.Count > 0 && ahora - _envios.Peek() >= TicksPorVentana)
                    _envios.Dequeue();

                if (_envios.Count < maximo)
                {
                    _envios.Enqueue(ahora);
                    return;
                }

                // Espera lo justo hasta que el envio mas viejo salga de la ventana. Si el tope
                // bajo y la ventana quedo con mas envios que el nuevo maximo, esto no rompe nada
                // ni entra en espera ocupada: simplemente vuelve a esperar turnos (cada uno del
                // tamaño de la ventana completa si hace falta) hasta que la cola se vacie por
                // debajo del tope vigente.
                var ticksRestantes = TicksPorVentana - (ahora - _envios.Peek());
                var espera = TimeSpan.FromSeconds((double)ticksRestantes / Stopwatch.Frequency);

                await Task.Delay(espera < TimeSpan.Zero ? TimeSpan.Zero : espera, ct);
            }
        }
        finally
        {
            _puerta.Release();
        }
    }

    public void Dispose() => _puerta.Dispose();
}
