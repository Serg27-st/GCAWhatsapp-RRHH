namespace RRHH.WhatsApp.Contracts.Bandeja;

/// <summary>
/// Respuesta del analista. Con <see cref="ClavePlantilla"/> vacia se manda texto libre, que solo
/// vale dentro de la ventana de 24h (Regla 15); fuera de ella hay que elegir una plantilla.
/// </summary>
/// <param name="ClaveIdempotencia">
/// La genera la bandeja por mensaje redactado y la conserva hasta que sale (V29). Si la peticion se
/// repite —doble clic, o un reintento tras perder la respuesta—, la Api devuelve el mensaje que ya
/// existe en vez de mandar otro. Vacia, cada peticion es un envio distinto.
/// </param>
public sealed record PeticionResponder(
    string? Texto,
    string? ClavePlantilla,
    IReadOnlyList<string>? Parametros,
    Guid ClaveIdempotencia = default);

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
    string? Motivo,
    /// <summary>A11: el cierre de cortesia sale por defecto al descartar; el analista puede decir que no.</summary>
    bool EnviarCierre = true);

/// <summary>
/// Regla 13: mueve la tarjeta entre columnas del tablero. La postulacion va en la ruta,
/// <c>POST /postulaciones/{id}/etapa</c>.
/// </summary>
public sealed record PeticionMoverEtapa(
    int EtapaId,
    /// <summary>A11: el cierre de cortesia sale por defecto al descartar; el analista puede decir que no.</summary>
    bool EnviarCierre = true);

/// <summary>
/// FUN-01: el analista toma un hilo de «Sin clasificar» para una de sus cuentas. La cuenta va en el
/// cuerpo porque es la decision que toma la persona: el bot no pudo identificarla.
/// </summary>
public sealed record PeticionTomar(int CuentaId);

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
public sealed record AnalistaResumen(
    int AnalistaId,
    string Nombre,
    string Email,
    string Rol,
    /// <summary>FUN-07: de vacaciones o con descanso medico hoy. La bandeja no lo ofrece como destino.</summary>
    bool Ausente = false);

public sealed record PeticionCrearCuenta(string Nombre);

/// <summary>FUN-20: corregir el nombre o desactivar la cuenta. Lo que viene nulo no se toca.</summary>
public sealed record PeticionEditarCuenta(string? Nombre, bool? Activo);

/// <summary>
/// FUN-20: corregir lo que se cargo mal en una vacante. Lo que viene nulo no se toca; el enlace tiene
/// que ser https y el codigo, unico.
/// </summary>
public sealed record PeticionEditarVacante(string? Titulo, string? UrlJobForms, string? CodigoAviso);

/// <summary>Regla 1 y Regla 2: titular cuando <c>EsBackup</c> es falso, respaldo cuando es verdadero.</summary>
public sealed record PeticionAsignarAnalista(int AnalistaId, bool EsBackup);

public sealed record PeticionCrearAnalista(string Nombre, string Email, string Rol);

/// <summary>
/// FUN-19: corregir el nombre, cambiar el rol o dar de baja. Lo que viene nulo no se toca, asi que la
/// pantalla manda solo lo que cambia.
/// </summary>
public sealed record PeticionEditarAnalista(string? Nombre, string? Rol, bool? Activo);

/// <summary>
/// FUN-19: lo que ese analista tiene encima hoy. Es lo que la pantalla muestra antes de darlo de baja:
/// cuantas conversaciones se van a reasignar y cuantas cuentas quedan sin quien las cubra.
/// </summary>
public sealed record CarteraAnalista(int Conversaciones, int CuentasTitular, int CuentasRespaldo);

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
/// Regla 8: transferencia esperando la respuesta del analista destino. Trae de quién viene, de qué
/// cuenta y sobre quién es, para que se pueda decidir sin abrir un chat que todavía no es suyo.
/// <para>
/// No lleva <c>Urgente</c>: una urgente se aplica sola y nunca queda esperando respuesta.
/// </para>
/// </summary>
public sealed record TransferenciaPendiente(
    int TransferenciaId,
    int ConversacionId,
    string AnalistaOrigen,
    string? Cuenta,
    string? NombrePostulante,
    string? TelefonoE164,
    string? Comentario,
    DateTime FechaUtc,
    /// <summary>A1: cuando vence si no la responde. La bandeja lo muestra para que se sepa cuanto queda.</summary>
    DateTime? FechaVencimiento = null);

/// <summary>
/// FUN-07: lo que el analista ofrecio y todavia espera respuesta. Es la otra mitad de la Regla 8: sin
/// verlo, no hay forma de retirar una transferencia que ya no corresponde.
/// </summary>
public sealed record TransferenciaEnviada(
    int TransferenciaId,
    int ConversacionId,
    string AnalistaDestino,
    string? NombrePostulante,
    DateTime FechaUtc,
    DateTime? FechaVencimiento);
