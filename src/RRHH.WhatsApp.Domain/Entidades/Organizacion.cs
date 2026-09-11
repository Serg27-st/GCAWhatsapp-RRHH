using RRHH.WhatsApp.Domain.Enums;

namespace RRHH.WhatsApp.Domain.Entidades;

/// <summary>Cliente al que RRHH recluta (ej. Intradevco, Alicorp, Tubisa). Unidad de enrutamiento de la Regla 1.</summary>
public class Cuenta
{
    public int CuentaId { get; set; }
    public required string Nombre { get; set; }
    public bool Activo { get; set; } = true;

    public ICollection<Hc> Vacantes { get; set; } = [];
    public ICollection<AnalistaCuenta> Asignaciones { get; set; } = [];
}

public class Analista
{
    public int AnalistaId { get; set; }
    public required string Nombre { get; set; }
    public required string Email { get; set; }

    /// <summary>Regla 4: el rol Sistemas ve todas las conversaciones; el rol Analista solo las suyas.</summary>
    public RolAnalista Rol { get; set; } = RolAnalista.Analista;

    public bool Activo { get; set; } = true;

    /// <summary>
    /// Hash de la contraseña (Sección 9.6.1). Nulo significa que este analista todavía no puede
    /// entrar: es lo correcto por omisión — un alta sin contraseña no debe dejar una cuenta
    /// abierta, sino una cuenta que aún no sirve.
    /// </summary>
    public string? HashContrasena { get; set; }

    /// <summary>Cuándo se cambió por última vez. Sirve para exigir rotación si algún día hace falta.</summary>
    public DateTime? FechaContrasena { get; set; }

    public ICollection<AnalistaCuenta> Cuentas { get; set; } = [];
    public ICollection<Ausencia> Ausencias { get; set; } = [];
}

/// <summary>
/// Relacion muchos a muchos entre analistas y cuentas. <see cref="EsBackup"/> marca al respaldo
/// fijo predefinido por cuenta que recibe las conversaciones escaladas (Regla 2) y las de un
/// analista ausente (Regla 14).
/// </summary>
public class AnalistaCuenta
{
    public int AnalistaCuentaId { get; set; }
    public int AnalistaId { get; set; }
    public int CuentaId { get; set; }
    public bool EsBackup { get; set; }

    public Analista? Analista { get; set; }
    public Cuenta? Cuenta { get; set; }
}

/// <summary>Regla 14: ausencia planificada (vacaciones, descanso medico) registrada con anticipacion.</summary>
public class Ausencia
{
    public int AusenciaId { get; set; }
    public int AnalistaId { get; set; }
    public DateTime FechaInicio { get; set; }
    public DateTime FechaFin { get; set; }
    public string? Motivo { get; set; }

    public Analista? Analista { get; set; }
}

/// <summary>
/// Regla 3: horario de atencion que dispara el mensaje automatico de fuera de horario.
/// <see cref="CuentaId"/> nulo significa horario general para toda la operacion.
/// </summary>
public class HorarioAtencion
{
    public int HorarioId { get; set; }
    public int? CuentaId { get; set; }
    public DayOfWeek DiaSemana { get; set; }
    public TimeOnly HoraInicio { get; set; }
    public TimeOnly HoraFin { get; set; }

    public Cuenta? Cuenta { get; set; }
}

/// <summary>
/// Vacante (head count). Es la unidad del tablero kanban y la duena del formulario de postulacion:
/// el codigo de JobForms vive aca y no en <see cref="Cuenta"/>, porque las preguntas especificas
/// varian por vacante (ver <see cref="HcCampoOpcional"/>).
/// </summary>
public class Hc
{
    public int HcId { get; set; }
    public int CuentaId { get; set; }
    public required string Titulo { get; set; }
    public EstadoHc Estado { get; set; } = EstadoHc.Abierta;

    /// <summary>Id del formulario de Google asociado a esta vacante. Al migrar a Razor Pages queda sin uso.</summary>
    public string? CodigoJobForms { get; set; }

    /// <summary>URL base del formulario, con los parametros de prellenado que arma IJobFormsInvitacionService.</summary>
    public string? UrlJobForms { get; set; }

    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaCierre { get; set; }

    public Cuenta? Cuenta { get; set; }
    public ICollection<HcCampoOpcional> CamposOpcionales { get; set; } = [];
}

/// <summary>Campos opcionales que el analista activa por vacante, segun lo pedido por la gerencia al definir el JobForms.</summary>
public class HcCampoOpcional
{
    public int CampoId { get; set; }
    public int HcId { get; set; }
    public required string NombreCampo { get; set; }
    public TipoCampoOpcional Tipo { get; set; }
    public bool Activo { get; set; } = true;

    public Hc? Hc { get; set; }
}
