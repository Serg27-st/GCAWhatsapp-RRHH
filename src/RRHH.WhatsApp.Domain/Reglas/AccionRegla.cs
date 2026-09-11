using RRHH.WhatsApp.Domain.Enums;

namespace RRHH.WhatsApp.Domain.Reglas;

/// <summary>
/// Lo que una regla decide que debe pasar. Las reglas no envian mensajes ni escriben en las tablas
/// de otros modulos: solo devuelven acciones, y la capa de aplicacion las ejecuta. Es lo que hace
/// que cada regla se pueda probar sin base de datos ni proveedor de WhatsApp.
/// </summary>
public abstract record AccionRegla;

/// <summary>Envia una plantilla aprobada por Meta, resuelta por su clave interna.</summary>
public sealed record EnviarPlantilla(string ClavePlantilla, IReadOnlyList<string> Parametros) : AccionRegla;

/// <summary>Envia texto libre. Solo valido con la ventana de servicio abierta (Regla 15).</summary>
public sealed record EnviarTextoLibre(string Texto) : AccionRegla;

/// <summary>Muestra el menu de empresas con botones. Regla 19 marca el reintento.</summary>
public sealed record MostrarMenuEmpresas(bool EsReintento) : AccionRegla;

public sealed record EnviarLinkJobForms(int HcId) : AccionRegla;

/// <summary>
/// Regla 9 y Regla 20: menu de vacantes abiertas de una cuenta. Aparece cuando la cuenta tiene
/// mas de un HC, porque el enlace del JobForms es por vacante y no por cliente.
/// </summary>
public sealed record MostrarMenuVacantes(int CuentaId) : AccionRegla;

public sealed record AsignarAnalista(int AnalistaId, string Motivo) : AccionRegla;

/// <summary>Regla 2 y Regla 14: pasa la conversacion al respaldo fijo de la cuenta.</summary>
public sealed record EscalarARespaldo(int AnalistaRespaldoId, string Motivo) : AccionRegla;

public sealed record EstablecerCuentaContexto(int CuentaId) : AccionRegla;

public sealed record CambiarEstadoConversacion(EstadoConversacion Estado) : AccionRegla;

public sealed record MoverEtapaKanban(int PostulacionId, int EtapaId) : AccionRegla;

/// <summary>Regla 6: aviso generico, sin detalle de mensajes, de que el postulante esta en otra cuenta.</summary>
public sealed record NotificarAnalista(int AnalistaId, string Mensaje) : AccionRegla;

public sealed record RegistrarOptIn(OrigenOptIn Origen) : AccionRegla;

/// <summary>Regla 16: el caso se archiva de forma definitiva.</summary>
public sealed record ArchivarConversacion(string Motivo) : AccionRegla;

/// <summary>
/// Regla 15: corta el envio saliente. Es la unica accion que detiene el pipeline;
/// las demas se acumulan y se ejecutan en orden.
/// </summary>
public sealed record BloquearEnvio(string Motivo) : AccionRegla;

/// <summary>Publica un evento en la outbox para que el Worker o Reporting lo recojan.</summary>
public sealed record PublicarEvento(string Tipo, object Payload) : AccionRegla;

public sealed record RegistrarAuditoria(string Accion, string Detalle) : AccionRegla;

/// <summary>
/// Regla 9: el bot vuelve a preguntar la empresa tras la inactividad configurada, asi que el
/// contexto anterior deja de valer. Sin esto no hay forma de deshacer un EstablecerCuentaContexto.
/// </summary>
public sealed record LimpiarCuentaContexto(string Motivo) : AccionRegla;

/// <summary>Regla 9: sella el recordatorio de 24h para que el proximo barrido no lo repita.</summary>
public sealed record MarcarRecordatorioJobForms(int InvitacionId) : AccionRegla;

/// <summary>Regla 9: sella el aviso al analista de 48h.</summary>
public sealed record MarcarAvisoAnalistaJobForms(int InvitacionId) : AccionRegla;

/// <summary>Resultado de evaluar una regla.</summary>
public sealed record ResultadoRegla(IReadOnlyList<AccionRegla> Acciones, bool DetenerEvaluacion = false)
{
    public static readonly ResultadoRegla SinAccion = new([]);

    public static ResultadoRegla Con(params AccionRegla[] acciones) => new(acciones);

    /// <summary>Para reglas terminales como la 15, que no deben dejar correr a las siguientes.</summary>
    public static ResultadoRegla Detener(params AccionRegla[] acciones) => new(acciones, DetenerEvaluacion: true);
}
