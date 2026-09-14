namespace RRHH.WhatsApp.Domain.Entidades;

/// <summary>
/// Señal de vida de un proceso en segundo plano (Sección 9.6.2).
/// <para>
/// Existe porque varias reglas —el escalamiento de 2h, el recordatorio de 24h, el aviso de 48h y
/// el archivado de 90 días— dependen enteramente de que el Worker siga corriendo, y un proceso
/// detenido no avisa: simplemente deja de pasar lo que tenía que pasar.
/// </para>
/// </summary>
public class LatidoServicio
{
    /// <summary>Nombre del bucle que late. Es la clave: cada uno lleva su propio ritmo.</summary>
    public required string Servicio { get; set; }

    public DateTime FechaUtc { get; set; }

    /// <summary>
    /// Cuánto puede tardar el próximo latido antes de considerarse detenido.
    /// <para>
    /// Lo declara quien late, no quien lo vigila: el barrido corre cada 5 minutos y la purga cada
    /// 24 horas, así que un único umbral global daría falsas alarmas en uno o silencio en el otro.
    /// Además evita que la Api tenga que conocer la configuración del Worker.
    /// </para>
    /// </summary>
    public int ToleranciaSegundos { get; set; }

    /// <summary>Qué hizo en el último ciclo. Es lo que distingue "vivo" de "vivo y trabajando".</summary>
    public string? Detalle { get; set; }
}

/// <summary>Nombres de los bucles que reportan latido. Constantes para no repartir cadenas sueltas.</summary>
public static class ServiciosVigilados
{
    public const string ConsumidorOutbox = "ConsumidorOutbox";
    public const string BarridoTiempo = "BarridoTiempo";
    public const string PurgaCv = "PurgaCv";
    public const string ReintentoEnvios = "ReintentoEnvios";

    /// <summary>
    /// La guardia que tiene el candado del Worker (V24). Su detalle dice que maquina y que proceso
    /// es la instancia activa: con dos Workers en juego, es lo primero que hay que saber.
    /// </summary>
    public const string InstanciaActiva = "InstanciaActiva";

    /// <summary>Lo que el health vigila del Worker. Si falta alguno, lo reporta como detenido.</summary>
    public static readonly IReadOnlyList<string> Todos =
        [ConsumidorOutbox, BarridoTiempo, PurgaCv, ReintentoEnvios, InstanciaActiva];
}
