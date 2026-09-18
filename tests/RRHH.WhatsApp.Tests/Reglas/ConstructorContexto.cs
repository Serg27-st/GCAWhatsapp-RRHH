using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Tests.Reglas;

/// <summary>
/// Arma contextos de prueba con valores por defecto razonables, para que cada test solo declare
/// lo que le importa. Las reglas no tocan base de datos ni proveedor, asi que esto es todo lo que
/// hace falta para probarlas.
/// </summary>
internal sealed class ConstructorContexto
{
    public static readonly DateTime Ahora = new(2026, 8, 19, 15, 0, 0, DateTimeKind.Utc);

    private TipoDisparador _disparador = TipoDisparador.MensajeEntrante;
    private DateTime _ahora = Ahora;
    private Conversacion? _conversacion;
    private Cuenta? _cuenta;
    private Analista? _titular;
    private Analista? _respaldo;
    private bool _titularAusente;
    private bool _dentroDeHorario = true;
    private int _intentosMenu;
    private double? _minutosReloj;
    private double? _minutosHabiles;
    private IReadOnlyList<PostulacionVigente> _postulaciones = [];
    private DateTime? _actividadAnterior;
    private DateTime? _inicioFueraHorario;
    private DateTime? _proximaApertura;
    private string? _descripcionHorario;
    private OrigenEleccion _origen = OrigenEleccion.Ninguna;
    private int _paginaMenu;
    private int? _postulacionElegidaId;
    private Transferencia? _transferenciaPendiente;
    private bool _enviarCierreSolicitado;
    private double? _minutosEscalamiento;
    private double? _minutosPendiente;
    private double? _minutosTextoNoReconocido;
    private readonly Dictionary<string, string> _config = [];

    public static Cuenta CuentaAlicorp => new() { CuentaId = 1, Nombre = "Alicorp" };
    public static Cuenta CuentaIntradevco => new() { CuentaId = 2, Nombre = "Intradevco" };

    public static Analista Titular => new()
    {
        AnalistaId = 10, Nombre = "Ana Torres", Email = "ana@gca.pe", Rol = RolAnalista.Analista
    };

    public static Analista Respaldo => new()
    {
        AnalistaId = 11, Nombre = "Luis Vega", Email = "luis@gca.pe", Rol = RolAnalista.Analista
    };

    public ConstructorContexto Disparador(TipoDisparador d) { _disparador = d; return this; }

    public ConstructorContexto En(DateTime ahora) { _ahora = ahora; return this; }

    public ConstructorContexto ConConversacion(
        EstadoConversacion estado = EstadoConversacion.Activa,
        int? analistaAtendiendoId = null,
        DateTime? optIn = null,
        DateTime? ultimoEntrante = null,
        int? cuentaContextoId = null,
        DateTime? avisoFueraHorario = null)
    {
        _conversacion = new Conversacion
        {
            ConversacionId = 100,
            TelefonoE164 = "+51987654321",
            Estado = estado,
            AnalistaAtendiendoId = analistaAtendiendoId,
            CuentaContextoId = cuentaContextoId,
            FechaOptIn = optIn,
            OrigenOptIn = optIn is null ? null : Domain.Enums.OrigenOptIn.MensajeEntrante,
            FechaUltimoMensajeEntrante = ultimoEntrante,
            FechaAvisoFueraHorario = avisoFueraHorario,
            FechaCreacion = _ahora.AddDays(-1),
            FechaUltimaActividad = _ahora
        };
        return this;
    }

    public ConstructorContexto ConCuenta(Cuenta cuenta) { _cuenta = cuenta; return this; }

    public ConstructorContexto ConTitular(Analista? a) { _titular = a; return this; }

    public ConstructorContexto ConRespaldo(Analista? a) { _respaldo = a; return this; }

    public ConstructorContexto TitularAusente(bool ausente = true) { _titularAusente = ausente; return this; }

    public ConstructorContexto FueraDeHorario() { _dentroDeHorario = false; return this; }

    public ConstructorContexto IntentosMenu(int n) { _intentosMenu = n; return this; }

    public ConstructorContexto SinResponderHace(double? minutosReloj, double? minutosHabiles = null)
    {
        _minutosReloj = minutosReloj;
        _minutosHabiles = minutosHabiles ?? minutosReloj;
        return this;
    }

    /// <summary>ARQ-07: los procesos del mismo DNI, que es lo que miran la R9, la R16 y la desambiguacion.</summary>
    public ConstructorContexto ConProcesos(params PostulacionVigente[] procesos)
    {
        _postulaciones = procesos;
        return this;
    }

    /// <summary>Un proceso armado con lo minimo: la cuenta, la vacante y en que quedo.</summary>
    public static PostulacionVigente Proceso(
        int postulacionId,
        EstadoPostulacion estado,
        int cuentaId = 1,
        string cuenta = "Alicorp",
        int hcId = 1,
        string vacante = "Operario de produccion",
        int? analistaAsignadoId = 10) =>
        new(postulacionId, cuentaId, cuenta, hcId, vacante, estado, analistaAsignadoId, Ahora.AddDays(-1));

    /// <summary>C1, A13: la instantanea que tomo el webhook antes de registrar el mensaje.</summary>
    public ConstructorContexto ActividadAnterior(DateTime? fecha) { _actividadAnterior = fecha; return this; }

    public ConstructorContexto EligioPor(OrigenEleccion origen) { _origen = origen; return this; }

    public ConstructorContexto PidioPagina(int pagina) { _paginaMenu = pagina; return this; }

    public ConstructorContexto EligioProceso(int postulacionId) { _postulacionElegidaId = postulacionId; return this; }

    public ConstructorContexto ConTransferenciaPendiente(Transferencia transferencia)
    {
        _transferenciaPendiente = transferencia;
        return this;
    }

    public ConstructorContexto CierreSolicitado(bool solicitado = true)
    {
        _enviarCierreSolicitado = solicitado;
        return this;
    }

    /// <summary>FUN-04: fuera de horario, desde cuando y hasta cuando, que es lo que el aviso necesita.</summary>
    public ConstructorContexto FueraDeHorarioDesde(
        DateTime inicio, DateTime proximaApertura, string? descripcion = "de lunes a viernes de 09:00 a 18:00")
    {
        _dentroDeHorario = false;
        _inicioFueraHorario = inicio;
        _proximaApertura = proximaApertura;
        _descripcionHorario = descripcion;
        return this;
    }

    /// <summary>FUN-05 y FUN-06: los plazos que corren en horas habiles, cada uno con su sello.</summary>
    public ConstructorContexto MinutosHabiles(
        double? desdeEscalamiento = null, double? enPendiente = null, double? desdeTextoNoReconocido = null)
    {
        _minutosEscalamiento = desdeEscalamiento;
        _minutosPendiente = enPendiente;
        _minutosTextoNoReconocido = desdeTextoNoReconocido;
        return this;
    }

    public ConstructorContexto Config(string clave, string valor) { _config[clave] = valor; return this; }

    public ContextoRegla Construir() => new()
    {
        Disparador = _disparador,
        AhoraUtc = _ahora,
        Conversacion = _conversacion,
        Cuenta = _cuenta,
        AnalistaTitular = _titular,
        AnalistaRespaldo = _respaldo,
        TitularAusente = _titularAusente,
        DentroDeHorario = _dentroDeHorario,
        IntentosMenuFallidos = _intentosMenu,
        MinutosSinRespuestaReloj = _minutosReloj,
        MinutosSinRespuestaHabiles = _minutosHabiles,
        PostulacionesDelPostulante = _postulaciones,
        FechaActividadAnterior = _actividadAnterior,
        InicioPeriodoFueraHorario = _inicioFueraHorario,
        ProximaApertura = _proximaApertura,
        DescripcionHorario = _descripcionHorario,
        OrigenEleccion = _origen,
        PaginaMenu = _paginaMenu,
        PostulacionElegidaId = _postulacionElegidaId,
        TransferenciaPendiente = _transferenciaPendiente,
        EnviarCierreSolicitado = _enviarCierreSolicitado,
        MinutosHabilesDesdeEscalamiento = _minutosEscalamiento,
        MinutosHabilesEnPendiente = _minutosPendiente,
        MinutosHabilesDesdeTextoNoReconocido = _minutosTextoNoReconocido,
        Configuracion = _config
    };
}
