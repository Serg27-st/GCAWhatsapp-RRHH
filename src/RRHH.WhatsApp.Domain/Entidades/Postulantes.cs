using RRHH.WhatsApp.Domain.Enums;

namespace RRHH.WhatsApp.Domain.Entidades;

/// <summary>
/// La persona. El DNI es el identificador principal (Regla 9) porque no cambia aunque cambie de celular;
/// por eso el telefono es un dato historico y no la clave.
/// </summary>
public class Postulante
{
    public int PostulanteId { get; set; }
    public required string Dni { get; set; }
    public string? NombreCompleto { get; set; }

    /// <summary>Ultimo telefono conocido, en formato E.164. Solo referencial: el hilo vive en <see cref="Conversacion"/>.</summary>
    public string? TelefonoUltimo { get; set; }

    public string? Email { get; set; }
    public DateTime FechaRegistro { get; set; }

    public ICollection<Postulacion> Postulaciones { get; set; } = [];
    public ICollection<EstadoPostulanteCuenta> Estados { get; set; } = [];
}

/// <summary>
/// Una persona postulando a una vacante concreta. Es la entidad que recorre el tablero kanban
/// (Regla 13) y la que hace posible la Regla 6: el mismo DNI en dos cuentas produce dos
/// postulaciones independientes, cada una visible solo para su analista, aunque WhatsApp
/// entregue un unico hilo de conversacion por numero de telefono.
/// </summary>
public class Postulacion
{
    public int PostulacionId { get; set; }
    public int PostulanteId { get; set; }
    public int HcId { get; set; }

    /// <summary>Denormalizado desde <see cref="Hc"/> para filtrar la bandeja por cuenta sin un join extra.</summary>
    public int CuentaId { get; set; }

    /// <summary>Analista dueno de la cuenta al momento de crear la postulacion (Regla 1).</summary>
    public int? AnalistaAsignadoId { get; set; }

    public int EtapaKanbanId { get; set; }
    public EstadoPostulacion Estado { get; set; } = EstadoPostulacion.EnProceso;

    public DateTime FechaCreacion { get; set; }
    public DateTime FechaUltimaActividad { get; set; }
    public DateTime? FechaCambioEtapa { get; set; }

    /// <summary>
    /// A11: el descarte pide cierre de cortesia pero todavia no salio —por ejemplo porque se descarto
    /// fuera de horario—. El barrido lo manda en horario laboral y una sola vez.
    /// </summary>
    public bool CierreCortesiaPendiente { get; set; }

    /// <summary>A11: sello del cierre de cortesia enviado. Descartar dos veces no lo repite (P4).</summary>
    public DateTime? FechaCierreCortesia { get; set; }

    /// <summary>A14: sello del aviso al analista antes de archivar por inactividad.</summary>
    public DateTime? FechaAvisoArchivado { get; set; }

    /// <summary>A2: cuando el analista marco el reingreso.</summary>
    public DateTime? FechaReingreso { get; set; }

    /// <summary>Control de concurrencia optimista: el Worker y el analista pueden tocar la misma fila (Seccion 9.6.2).</summary>
    public byte[]? RowVersion { get; set; }

    public Postulante? Postulante { get; set; }
    public Hc? Hc { get; set; }
    public Cuenta? Cuenta { get; set; }
    public Analista? AnalistaAsignado { get; set; }
    public EtapaKanban? EtapaKanban { get; set; }
}

/// <summary>Regla 7: marca de destacado o descartado, por postulante y cuenta. El motivo es obligatorio en blacklist.</summary>
public class EstadoPostulanteCuenta
{
    public int EstadoId { get; set; }
    public int PostulanteId { get; set; }
    public int CuentaId { get; set; }
    public TipoEstadoPostulante Tipo { get; set; }
    public string? Motivo { get; set; }
    public int AnalistaId { get; set; }
    public DateTime Fecha { get; set; }

    public Postulante? Postulante { get; set; }
    public Cuenta? Cuenta { get; set; }
    public Analista? Analista { get; set; }
}

/// <summary>Catalogo de columnas del tablero (Regla 13). <see cref="EsFinal"/> marca Contratado y Descartado.</summary>
public class EtapaKanban
{
    public int EtapaId { get; set; }
    public required string Nombre { get; set; }
    public int Orden { get; set; }
    public bool EsFinal { get; set; }

    /// <summary>
    /// El desenlace que implica llegar a esta columna (COR-11). Antes se deducia por el nombre de la
    /// etapa, y renombrar «Descartado» en la administracion rompia el cierre de cortesia. Nulo en las
    /// columnas intermedias.
    /// </summary>
    public EstadoPostulacion? EstadoResultante { get; set; }
}
