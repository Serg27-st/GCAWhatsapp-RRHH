namespace RRHH.WhatsApp.Domain.Enums;

/// <summary>Distingue a un analista normal del area de Sistemas, que tiene visibilidad total (Regla 4).</summary>
public enum RolAnalista
{
    Analista = 1,
    Sistemas = 2
}

/// <summary>
/// Estado del hilo de WhatsApp. <see cref="PendienteClasificar"/> soporta la bandeja general
/// de la Regla 19, cuando el bot no logro identificar la cuenta.
/// </summary>
public enum EstadoConversacion
{
    Activa = 1,
    Escalada = 2,
    PendienteClasificar = 3,
    Cerrada = 4,
    Archivada = 5
}

public enum DireccionMensaje
{
    Entrante = 1,
    Saliente = 2
}

public enum EstadoEntrega
{
    Pendiente = 1,
    Enviado = 2,
    Entregado = 3,
    Leido = 4,
    Fallido = 5
}

/// <summary>Regla 7. El motivo es obligatorio para <see cref="Blacklist"/> y opcional para <see cref="Whitelist"/>.</summary>
public enum TipoEstadoPostulante
{
    Whitelist = 1,
    Blacklist = 2
}

/// <summary>Regla 8. Permite distinguir una transferencia enviada de una efectivamente tomada.</summary>
public enum EstadoTransferencia
{
    Pendiente = 1,
    Aceptada = 2,
    Rechazada = 3
}

/// <summary>Regla 20. Una vacante cerrada desactiva su enlace de JobForms.</summary>
public enum EstadoHc
{
    Abierta = 1,
    Cerrada = 2
}

/// <summary>Categoria tarifaria de Meta. Determina el costo y si el envio requiere ventana abierta.</summary>
public enum CategoriaPlantilla
{
    Utilidad = 1,
    Marketing = 2,
    Servicio = 3,
    Autenticacion = 4
}

/// <summary>
/// Por que fallo un envio saliente. Es lo que decide si reintentarlo es seguro, util, o ninguna
/// de las dos cosas.
/// </summary>
public enum ClaseFallo
{
    Ninguno = 0,

    /// <summary>
    /// El proveedor no llego a procesar la peticion: 5xx, 429 o un fallo de conexion. El mensaje
    /// no salio, asi que reintentarlo no puede duplicar nada.
    /// </summary>
    Transitorio = 1,

    /// <summary>
    /// Credenciales invalidas, payload rechazado, plantilla sin aprobar. Reintentar da el mismo
    /// resultado y solo gasta cuota contra Meta.
    /// </summary>
    Permanente = 2,

    /// <summary>
    /// Se perdio la respuesta y no se sabe si Meta acepto el mensaje. La Cloud API no admite una
    /// clave de idempotencia, de modo que un reintento puede entregar el mismo mensaje dos veces
    /// — justo lo que este proyecto existe para evitar. No se reintenta solo: queda para que una
    /// persona decida.
    /// </summary>
    Ambiguo = 3
}

/// <summary>Estado de un registro de la tabla outbox (EventosSistema).</summary>
public enum EstadoEvento
{
    Pendiente = 1,
    Procesado = 2,
    Fallido = 3
}

/// <summary>
/// Regla 15. Como se obtuvo el consentimiento para escribirle al postulante.
/// Sin un valor registrado, el sistema bloquea todo envio saliente.
/// </summary>
public enum OrigenOptIn
{
    MensajeEntrante = 1,
    JobFormsCompletado = 2,
    RegistroManual = 3
}

/// <summary>Desenlace de una postulacion concreta (postulante + vacante), independiente del hilo de WhatsApp.</summary>
public enum EstadoPostulacion
{
    EnProceso = 1,
    Contratado = 2,
    Descartado = 3,
    Archivada = 4
}

/// <summary>Tipos admitidos para los campos opcionales que el analista activa por HC.</summary>
public enum TipoCampoOpcional
{
    Texto = 1,
    Numero = 2,
    Fecha = 3,
    Booleano = 4,
    Seleccion = 5
}
