using System.Diagnostics;

namespace RRHH.WhatsApp.Infrastructure.Proveedores;

/// <summary>
/// Limita cuantos mensajes salen por segundo hacia Meta (Seccion 9.6.4).
/// <para>
/// No es una optimizacion: el volumen saliente sin control es una de las tres causas del bloqueo
/// original descritas en la Seccion 2.4. Ventana deslizante de un segundo sobre los ultimos
/// envios; cuando se llena, el llamador espera en vez de descartarse, porque perder un mensaje
/// de RRHH es peor que enviarlo unos milisegundos despues.
/// </para>
/// </summary>
public sealed class LimitadorEnvio(int maximoPorSegundo) : IDisposable
{
    private static readonly long TicksPorVentana = Stopwatch.Frequency;

    private readonly int _maximo = maximoPorSegundo > 0 ? maximoPorSegundo : 1;
    private readonly SemaphoreSlim _puerta = new(1, 1);
    private readonly Queue<long> _envios = new();

    public int MaximoPorSegundo => _maximo;

    public async Task EsperarTurnoAsync(CancellationToken ct = default)
    {
        await _puerta.WaitAsync(ct);
        try
        {
            while (true)
            {
                var ahora = Stopwatch.GetTimestamp();

                // Descarta los envios que ya salieron de la ventana de un segundo.
                while (_envios.Count > 0 && ahora - _envios.Peek() >= TicksPorVentana)
                    _envios.Dequeue();

                if (_envios.Count < _maximo)
                {
                    _envios.Enqueue(ahora);
                    return;
                }

                // Espera lo justo hasta que el envio mas viejo salga de la ventana.
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
