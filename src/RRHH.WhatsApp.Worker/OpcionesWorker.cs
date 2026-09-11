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

    /// <summary>Pausa tras fallar el lote completo, para no golpear un recurso que ya esta caido.</summary>
    public int PausaTrasErrorSegundos { get; set; } = 30;
}
