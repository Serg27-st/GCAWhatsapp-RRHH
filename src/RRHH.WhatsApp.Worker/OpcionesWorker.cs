namespace RRHH.WhatsApp.Worker;

/// <summary>
/// Cadencia del Worker: cada cuanto mira la cola y de a cuantos elementos trabaja.
/// <para>
/// Viven en appsettings y no en <c>ConfiguracionReglas</c> a proposito: son parametros de
/// operacion, no reglas de negocio. Las 2 horas del escalamiento, los 3 dias de repregunta y los
/// 90 dias de archivado los definio la gerencia; esto otro es de que tan rapido late el proceso,
/// y ajustarlo es una decision de quien lo opera.
/// </para>
/// </summary>
public sealed class OpcionesWorker
{
    public const string Seccion = "Worker";

    /// <summary>Eventos que se sacan de la outbox por vuelta.</summary>
    public int TamanoLoteOutbox { get; set; } = 50;

    /// <summary>Pausa entre vueltas cuando la cola quedo vacia.</summary>
    public int IntervaloOutboxSegundos { get; set; } = 5;

    /// <summary>Cada cuanto se revisan las conversaciones que podrian tener que escalar (Regla 2).</summary>
    public int IntervaloBarridoMinutos { get; set; } = 5;

    /// <summary>Conversaciones que mira cada barrido.</summary>
    public int TamanoLoteBarrido { get; set; } = 200;

    /// <summary>Cada cuanto corre la purga de CVs (Regla 17). Es mantenimiento diario, no urgente.</summary>
    public int IntervaloPurgaHoras { get; set; } = 24;

    /// <summary>CVs que revisa cada purga.</summary>
    public int TamanoLotePurga { get; set; } = 500;

    /// <summary>Cada cuanto se reintentan los salientes que fallaron por causa transitoria.</summary>
    public int IntervaloReintentoMinutos { get; set; } = 2;

    /// <summary>Mensajes que reintenta cada vuelta.</summary>
    public int TamanoLoteReintento { get; set; } = 50;

    /// <summary>Pausa entre vueltas del despachador de envios cuando la cola quedo vacia (V29).</summary>
    public int IntervaloDespachoSegundos { get; set; } = 2;

    /// <summary>Mensajes encolados que toma cada vuelta del despachador.</summary>
    public int TamanoLoteDespacho { get; set; } = 20;

    /// <summary>
    /// Cuanto puede quedar un mensaje Enviando antes de darlo por perdido y marcarlo ambiguo. Tiene
    /// que superar holgadamente el timeout HTTP del adaptador: marcar ambiguo un envio en curso lo
    /// dejaria fuera del hilo aunque haya salido bien.
    /// </summary>
    public int TimeoutEnviandoSegundos { get; set; } = 120;

    /// <summary>
    /// Pausa entre vueltas de la descarga de adjuntos cuando no quedo nada por bajar (V33). Corta: el
    /// id de medio caduca, y el analista espera ver el CV poco despues de que llega.
    /// </summary>
    public int IntervaloDescargaAdjuntosSegundos { get; set; } = 30;

    /// <summary>Adjuntos que baja cada vuelta. Cada uno puede pesar varios megas.</summary>
    public int TamanoLoteDescargaAdjuntos { get; set; } = 10;

    /// <summary>Intentos por adjunto antes de darlo por perdido.</summary>
    public int IntentosDescargaAdjunto { get; set; } = 5;

    /// <summary>Espera tras el primer fallo de una descarga; se duplica en cada intento (1, 2, 4, 8 min).</summary>
    public int EsperaReintentoDescargaSegundos { get; set; } = 60;

    /// <summary>Cuanto puede tardar un adjunto, bajada y antivirus incluidos, antes de cortarlo y reintentar.</summary>
    public int TiempoMaximoDescargaSegundos { get; set; } = 300;

    /// <summary>Pausa tras fallar el lote completo, para no golpear un recurso que ya esta caido.</summary>
    public int PausaTrasErrorSegundos { get; set; } = 30;

    /// <summary>
    /// Cada cuanto reintenta tomar el candado una instancia en espera (V24). Es lo que tarda la de
    /// reserva en tomar el relevo cuando la activa muere.
    /// </summary>
    public int EsperaCandadoSegundos { get; set; } = 15;

    /// <summary>
    /// Cada cuanto la instancia activa comprueba que el candado sigue siendo suyo. Es la ventana en
    /// que, tras perder la conexion, podria seguir procesando sin saberlo: conviene que sea menor que
    /// <see cref="EsperaCandadoSegundos"/>, para que la activa se entere antes de que otra lo tome.
    /// </summary>
    public int VerificacionCandadoSegundos { get; set; } = 5;
}
