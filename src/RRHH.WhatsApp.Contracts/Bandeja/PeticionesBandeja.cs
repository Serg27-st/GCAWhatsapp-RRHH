namespace RRHH.WhatsApp.Contracts.Bandeja;

/// <summary>
/// Respuesta del analista. Con <see cref="ClavePlantilla"/> vacia se manda texto libre, que solo
/// vale dentro de la ventana de 24h (Regla 15); fuera de ella hay que elegir una plantilla.
/// </summary>
public sealed record PeticionResponder(
    string? Texto,
    string? ClavePlantilla,
    IReadOnlyList<string>? Parametros);

/// <summary>Lo que la bandeja necesita saber tras intentar responder.</summary>
public sealed record ResultadoResponder(
    bool Enviado,
    string? Motivo,
    /// <summary>true cuando el rechazo se resuelve eligiendo una plantilla, no reintentando el texto.</summary>
    bool RequierePlantilla,
    long? MensajeId);

/// <summary>Regla 8: transferencia a otro analista, de uno en uno.</summary>
public sealed record PeticionTransferir(
    int AnalistaDestinoId,
    bool Urgente,
    string? Comentario);

public sealed record PeticionResponderTransferencia(bool Aceptada);

/// <summary>Regla 7: el motivo es obligatorio para blacklist y opcional para whitelist.</summary>
public sealed record PeticionMarcar(
    int PostulanteId,
    int CuentaId,
    string Tipo,
    string? Motivo);

/// <summary>Regla 13: mueve la tarjeta entre columnas del tablero.</summary>
public sealed record PeticionMoverEtapa(int PostulacionId, int EtapaId);

public sealed record PeticionCrearVacante(int CuentaId, string Titulo, string? UrlJobForms);

public sealed record PeticionAusencia(DateTime FechaInicio, DateTime FechaFin, string? Motivo);

/// <summary>Regla 3: un tramo del horario de atencion.</summary>
public sealed record TramoHorario(DayOfWeek DiaSemana, TimeOnly HoraInicio, TimeOnly HoraFin);

public sealed record PlantillaResumen(
    int PlantillaId,
    string Clave,
    string NombreMeta,
    string Categoria,
    string TextoAprobado,
    int Parametros,
    bool Activa);

public sealed record CuentaDeAnalista(int CuentaId, string Nombre, bool EsBackup, int VacantesAbiertas);

/// <summary>Analista, para el selector de destino de una transferencia y el filtro de la bandeja.</summary>
public sealed record AnalistaResumen(int AnalistaId, string Nombre, string Email, string Rol);

public sealed record PeticionCrearCuenta(string Nombre);

/// <summary>Regla 1 y Regla 2: titular cuando <c>EsBackup</c> es falso, respaldo cuando es verdadero.</summary>
public sealed record PeticionAsignarAnalista(int AnalistaId, bool EsBackup);

public sealed record PeticionCrearAnalista(string Nombre, string Email, string Rol);

/// <summary>Cuenta con su dotación, que es lo que hay que revisar antes de abrirle vacantes.</summary>
public sealed record CuentaDetalle(
    int CuentaId,
    string Nombre,
    bool Activo,
    AnalistaResumen? Titular,
    AnalistaResumen? Respaldo,
    int VacantesAbiertas);

/// <summary>
/// Campo opcional que el analista activa por vacante (Sección 9.2). Se suma a los campos fijos del
/// JobForms; el tipo le dice al formulario qué control mostrar.
/// </summary>
public sealed record CampoOpcional(string NombreCampo, string Tipo, bool Activo);

/// <summary>
/// Regla 8: transferencia esperando la respuesta del analista destino. Trae de quién viene y sobre
/// quién es, para que se pueda decidir sin abrir el chat.
/// </summary>
public sealed record TransferenciaPendiente(
    int TransferenciaId,
    int ConversacionId,
    string AnalistaOrigen,
    string? NombrePostulante,
    string? TelefonoE164,
    bool Urgente,
    string? Comentario,
    DateTime FechaUtc);
