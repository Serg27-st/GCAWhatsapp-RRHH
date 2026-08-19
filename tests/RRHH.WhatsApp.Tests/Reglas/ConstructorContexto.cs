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
    private IReadOnlyList<Cuenta> _otrasCuentas = [];
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
        int? cuentaContextoId = null)
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

    public ConstructorContexto EnOtrasCuentas(params Cuenta[] cuentas) { _otrasCuentas = cuentas; return this; }

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
        OtrasCuentasEnProceso = _otrasCuentas,
        Configuracion = _config
    };
}
