using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Domain.Interfaces;

/// <summary>
/// Avisos para una persona, agrupados (V32). Reemplazan a los eventos de la outbox que nadie consumia
/// (M1): una vacante sin formulario o una plantilla sin aprobar no son trabajo para un consumidor,
/// son algo que alguien tiene que arreglar.
/// </summary>
public interface IAlertaOperativaService
{
    /// <summary>Suma una ocurrencia a la alerta abierta de ese tipo y clave, o la abre. Sin datos personales en el detalle.</summary>
    Task RegistrarAsync(string tipo, string clave, string detalle, CancellationToken ct = default);

    Task<IReadOnlyList<AlertaOperativa>> ListarAbiertasAsync(CancellationToken ct = default);

    /// <summary>False si no existe o ya estaba resuelta.</summary>
    Task<bool> ResolverAsync(int alertaId, int analistaId, CancellationToken ct = default);
}

/// <summary>
/// Frontera transaccional explicita de un caso de uso (V28, ARQ-02). Los servicios siguen guardando
/// con su propio SaveChanges; dentro de <see cref="EjecutarAsync"/> esos guardados se confirman o se
/// deshacen juntos. Sin esto, un fallo a mitad del webhook o de la outbox dejaba escrita la primera
/// parte y el reintento reprocesaba sobre un estado a medias (C5, C6).
/// </summary>
public interface IUnidadTrabajo
{
    /// <summary>
    /// Ejecuta <paramref name="trabajo"/> en una transaccion. Si ya hay una abierta en el mismo
    /// ambito la reutiliza sin confirmarla: la confirma quien la abrio. Si el trabajo lanza, se
    /// deshace y la excepcion sale tal cual.
    /// </summary>
    Task EjecutarAsync(Func<CancellationToken, Task> trabajo, CancellationToken ct = default);

    /// <inheritdoc cref="EjecutarAsync(Func{CancellationToken, Task}, CancellationToken)"/>
    Task<T> EjecutarAsync<T>(Func<CancellationToken, Task<T>> trabajo, CancellationToken ct = default);
}

/// <summary>
/// Motivos que devuelven los servicios, la Api y la bandeja con el mismo texto: son la misma regla
/// vista desde tres lados, y escribirlos por separado los deja diciendo cosas distintas.
/// </summary>
public static class MotivosBandeja
{
    /// <summary>FUN-01 (P3): responder, transferir o marcar exigen tomar antes el hilo de la bandeja general.</summary>
    public const string TomarPrimero = "Toma la conversacion antes de actuar: todavia esta en «Sin clasificar».";
}

/// <summary>
/// Unico punto de acceso a la tabla de Conversaciones. El resto del sistema pasa por aca y
/// nunca toca sus tablas directamente.
/// </summary>
public interface IConversacionService
{
    /// <summary>Busca el hilo por telefono o lo crea. El postulante puede no existir aun: el DNI llega despues.</summary>
    Task<Conversacion> ObtenerOCrearAsync(string telefonoE164, CancellationToken ct = default);

    Task<Conversacion?> ObtenerPorIdAsync(int conversacionId, CancellationToken ct = default);

    /// <summary>
    /// Actualiza el hilo tras un mensaje entrante: abre la ventana de 24h, refresca la actividad
    /// y registra el opt-in si es la primera vez (Regla 15).
    /// </summary>
    Task RegistrarEntradaAsync(int conversacionId, DateTime fechaUtc, CancellationToken ct = default);

    /// <summary>Regla 1: enruta la conversacion al analista dueno de la cuenta identificada.</summary>
    Task AsignarAnalistaAsync(int conversacionId, int analistaId, string motivo, CancellationToken ct = default);

    /// <summary>
    /// Regla 2 y 14: pasa la conversacion al respaldo fijo de la cuenta.
    /// <para>
    /// <paramref name="analistaEsperadoId"/> es quien la atendia cuando se decidio escalar (COR-13). Si
    /// ya no es el mismo, o si el titular respondio mientras tanto, no escala: el barrido lee y decide
    /// en un ambito y ejecuta en otro, y en el medio el analista pudo haber contestado (M4).
    /// </para>
    /// </summary>
    Task EscalarAsync(
        int conversacionId, int analistaEsperadoId, int analistaRespaldoId, string motivo,
        CancellationToken ct = default);

    /// <summary>Regla 8: deriva a otro analista, de uno en uno.</summary>
    Task<Transferencia> TransferirAsync(
        int conversacionId, int analistaOrigenId, int analistaDestinoId,
        bool urgente, string? comentario, CancellationToken ct = default);

    Task ResponderTransferenciaAsync(
        int transferenciaId, int analistaDestinoId, bool aceptada, CancellationToken ct = default);

    /// <summary>
    /// Regla 8: transferencias esperando la respuesta de un analista.
    /// <para>
    /// Sin esto el destino recibe el aviso de que le transfirieron algo pero no tiene dónde verlo,
    /// y una transferencia que requiere aceptación se queda Pendiente para siempre.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<Transferencia>> ListarTransferenciasPendientesAsync(
        int analistaDestinoId, CancellationToken ct = default);

    /// <summary>
    /// FUN-07: quien envio una transferencia que nadie respondio todavia se arrepiente y la retira. El
    /// hilo sigue siendo suyo; el destino recibe el aviso de que ya no tiene que responderla.
    /// </summary>
    Task RetirarTransferenciaAsync(int transferenciaId, int analistaOrigenId, CancellationToken ct = default);

    /// <summary>FUN-07: lo que el analista ofrecio y todavia espera respuesta, para poder retirarlo.</summary>
    Task<IReadOnlyList<Transferencia>> ListarTransferenciasEnviadasPendientesAsync(
        int analistaOrigenId, CancellationToken ct = default);

    /// <summary>Fija sobre que cuenta se esta conversando ahora, tras la eleccion en el menu del bot.</summary>
    Task EstablecerCuentaContextoAsync(int conversacionId, int cuentaId, CancellationToken ct = default);

    /// <summary>
    /// Regla 9: descarta la cuenta en contexto cuando el postulante reaparece tras varios dias,
    /// para que el bot pueda volver a preguntarle la empresa.
    /// </summary>
    Task LimpiarCuentaContextoAsync(int conversacionId, string motivo, CancellationToken ct = default);

    /// <summary>
    /// Regla 9: enlaza el hilo con la persona una vez que el DNI llega por el formulario. Hasta
    /// ese momento la conversacion solo conoce un telefono (desviacion V2 de docs/decisiones.md).
    /// </summary>
    Task VincularPostulanteAsync(int conversacionId, int postulanteId, CancellationToken ct = default);

    /// <summary>Regla 15: deja registrado el consentimiento sin el cual se bloquea todo envio.</summary>
    Task RegistrarOptInAsync(int conversacionId, OrigenOptIn origen, CancellationToken ct = default);

    /// <summary>
    /// Marca que un analista respondio. Es lo que detiene el reloj de la Regla 2: sin esto, el
    /// Worker escalaria conversaciones que ya fueron atendidas.
    /// </summary>
    Task RegistrarRespuestaAnalistaAsync(int conversacionId, DateTime fechaUtc, CancellationToken ct = default);

    Task CambiarEstadoAsync(int conversacionId, EstadoConversacion estado, CancellationToken ct = default);

    /// <summary>Bandeja del analista. El rol Sistemas ve todas las conversaciones (Regla 4).</summary>
    Task<IReadOnlyList<Conversacion>> ListarParaAnalistaAsync(int analistaId, CancellationToken ct = default);

    /// <summary>
    /// Buscador por DNI de la Seccion 7: trae el hilo del postulante, si ya se identifico. Respeta
    /// la Regla 4 igual que la bandeja: un analista encuentra lo que atiende; Sistemas, todo.
    /// </summary>
    Task<IReadOnlyList<Conversacion>> BuscarPorDniAsync(
        string dni, int analistaId, CancellationToken ct = default);

    /// <summary>
    /// Regla 4: que puede hacer el analista con este hilo. La Api lo consulta antes de cualquier
    /// accion sobre una conversacion: tener sesion no alcanza para leer o responder una ajena.
    /// </summary>
    Task<NivelAcceso> ObtenerAccesoAsync(int conversacionId, int analistaId, CancellationToken ct = default);


    /// <summary>
    /// FUN-01 (AL1, A7): un analista toma un hilo de «Sin clasificar» para su cuenta. Es el unico
    /// camino por el que una conversacion sale de la bandeja general, y por eso exige acceso total a
    /// esa cuenta (Regla 4): tomarla es adjudicarsela.
    /// <para>
    /// Lanza <see cref="Excepciones.ConflictoConcurrenciaException"/> si otro la tomo primero, y
    /// <see cref="InvalidOperationException"/> si el hilo ya no esta en «Sin clasificar».
    /// </para>
    /// </summary>
    Task<Conversacion> TomarAsync(
        int conversacionId, int analistaId, int cuentaId, CancellationToken ct = default);
    /// <summary>Regla 19: bandeja general de pendientes por clasificar, visible para todos.</summary>
    Task<IReadOnlyList<Conversacion>> ListarPendientesClasificarAsync(CancellationToken ct = default);

    /// <summary>
    /// Regla 2: hilos con un mensaje del postulante todavia sin responder, que son los candidatos
    /// a escalar. Devuelve solo los identificadores porque el barrido recarga cada conversacion en
    /// su propio ambito; y decide la regla, no esta consulta, si las 2 horas ya se cumplieron.
    /// </summary>
    Task<IReadOnlyList<int>> ListarPendientesEscalamientoAsync(int maximo, CancellationToken ct = default);

    /// <summary>
    /// FUN-05 (A9): hilos escalados que siguen sin respuesta y todavia no llegaron a Jefatura. Igual
    /// que el escalamiento, esto solo prefiltra: el plazo en horas habiles lo decide la regla.
    /// </summary>
    Task<IReadOnlyList<int>> ListarPendientesSegundoNivelAsync(int maximo, CancellationToken ct = default);

    /// <summary>
    /// FUN-12 (A10): cuantas conversaciones de esas cuentas se asignaron por ausencia del titular en
    /// el periodo y siguen sin volver a el. Es el numero del aviso de retorno.
    /// </summary>
    Task<int> ContarAsignadasPorAusenciaAsync(
        IReadOnlyCollection<int> cuentaIds, int titularId, DateTime desdeUtc, DateTime hastaUtc,
        CancellationToken ct = default);

    /// <summary>
    /// FUN-19: pasa lo que atiende ese analista a quien corresponda —el respaldo de la cuenta, o el
    /// titular si el era el respaldo— y resuelve sus transferencias pendientes. Sin reemplazo, el hilo
    /// va a «Sin clasificar», que es preferible a dejarlo con alguien que ya no esta (P3). Devuelve
    /// cuantas conversaciones se movieron.
    /// </summary>
    Task<int> ReasignarCarteraAsync(int analistaId, int autorId, CancellationToken ct = default);

    /// <summary>
    /// FUN-16: reemplaza el telefono de los hilos de ese postulante por <c>ANON-{id}</c> y devuelve
    /// cuales fueron. El telefono es el dato personal del hilo y ademas su clave: si la persona vuelve a
    /// escribir, nace una conversacion nueva con opt-in nuevo, que es lo correcto despues de un pedido
    /// de eliminacion.
    /// </summary>
    Task<IReadOnlyList<int>> AnonimizarPorPostulanteAsync(int postulanteId, CancellationToken ct = default);

    /// <summary>
    /// FUN-07 (A1): la transferencia no urgente que nadie respondio pasa a <see cref="EstadoTransferencia.Vencida"/>
    /// y el hilo sigue con quien la envio. No hace nada si dejo de estar pendiente mientras tanto.
    /// </summary>
    Task VencerTransferenciaAsync(int transferenciaId, CancellationToken ct = default);

    /// <summary>FUN-07: conversaciones con una transferencia no urgente ya vencida, para el barrido.</summary>
    Task<IReadOnlyList<int>> ListarConversacionesConTransferenciaVencidaAsync(
        DateTime ahora, int maximo, CancellationToken ct = default);

    /// <summary>
    /// FUN-06 (A12): hilos en el menu del bot con un texto que no reconocio. Prefiltro del barrido;
    /// cuanto silencio hace falta para derivarlos lo decide la regla, en horas habiles.
    /// </summary>
    Task<IReadOnlyList<int>> ListarPendientesDerivacionMenuAsync(int maximo, CancellationToken ct = default);

    /// <summary>FUN-06 (P3): hilos en «Sin clasificar» que nadie tomo y a los que todavia no se aviso.</summary>
    Task<IReadOnlyList<int>> ListarPendientesAvisoClasificacionAsync(int maximo, CancellationToken ct = default);

    /// <summary>
    /// Regla 16: hilos sin actividad desde hace mas de <paramref name="diasSinActividad"/> y que
    /// todavia no estan archivados. Igual que en el escalamiento, esto solo prefiltra candidatos:
    /// si corresponde archivar o no lo decide la regla, que es la que mira las postulaciones.
    /// </summary>
    Task<IReadOnlyList<int>> ListarPendientesArchivadoAsync(
        int diasSinActividad, int maximo, CancellationToken ct = default);

    /// <summary>FUN-04 a FUN-06: sella el aviso que acaba de salir para que el proximo barrido no lo repita (P4).</summary>
    Task SellarAsync(int conversacionId, MarcaConversacion marca, CancellationToken ct = default);

    /// <summary>
    /// COR-06: suma un intento del menu sin opcion valida. Con <paramref name="textoNoReconocido"/> sella
    /// ademas la fecha, que es desde donde corre el plazo para derivar por silencio (A12).
    /// </summary>
    Task RegistrarIntentoMenuAsync(int conversacionId, bool textoNoReconocido, CancellationToken ct = default);

    /// <summary>COR-06: el postulante eligio o lo tomo una persona; el contador del menu vuelve a cero.</summary>
    Task ReiniciarIntentosMenuAsync(int conversacionId, CancellationToken ct = default);

    /// <summary>FUN-06 (P3): pasa el hilo a «Sin clasificar», sella desde cuando espera y deja rastro.</summary>
    Task DerivarAPendientesAsync(int conversacionId, string motivo, CancellationToken ct = default);

    /// <summary>FUN-11: saca el hilo de Archivada porque el postulante volvio a escribir.</summary>
    Task ReactivarAsync(int conversacionId, EstadoConversacion nuevo, CancellationToken ct = default);

    /// <summary>
    /// FUN-08, FUN-09: fija la cuenta de la postulacion y su analista asignado —o el titular de la cuenta
    /// si no tiene—, para que quien ya esta en un proceso no vuelva a pasar por el menu del bot.
    /// </summary>
    Task TomarContextoDePostulacionAsync(int conversacionId, int postulacionId, CancellationToken ct = default);
}


/// <summary>
/// Catalogo de analistas. La bandeja lo necesita para ofrecer destinos de transferencia (Regla 8)
/// y para saber quien tiene el rol Sistemas, que ve todo (Regla 4).
/// </summary>
public interface IAnalistaService
{
    Task<IReadOnlyList<Analista>> ListarActivosAsync(CancellationToken ct = default);

    Task<Analista?> ObtenerPorIdAsync(int analistaId, CancellationToken ct = default);

    /// <summary>
    /// FUN-05, FUN-06: a quien avisar cuando el destinatario es un rol y no una persona. Quien esta hoy
    /// en Jefatura cambia sin que cambie la regla, asi que la regla nombra el rol y esto lo resuelve.
    /// </summary>
    Task<IReadOnlyList<Analista>> ListarActivosPorRolAsync(RolAnalista rol, CancellationToken ct = default);

    /// <summary>Alta de analista. El email es unico y es lo que lo identificara cuando haya login.</summary>
    Task<Analista> CrearAsync(
        string nombre, string email, RolAnalista rol, CancellationToken ct = default);

    /// <summary>
    /// ARQ-11 (V34): lo que la Api necesita saber de cada token en cada peticion. Nulo si el analista ya
    /// no existe.
    /// </summary>
    Task<EstadoSeguridad?> ObtenerEstadoSeguridadAsync(int analistaId, CancellationToken ct = default);

    /// <summary>
    /// FUN-18: deja sin efecto los tokens que ese analista ya tenga —un equipo perdido, alguien que se
    /// fue— subiendo su version de seguridad. False si no existe.
    /// </summary>
    Task<bool> CerrarSesionesAsync(int analistaId, CancellationToken ct = default);

    /// <summary>
    /// FUN-19: que tiene encima ese analista. Es lo que la pantalla muestra antes de darlo de baja:
    /// cuantas conversaciones se van a reasignar y cuantas cuentas quedan sin quien las cubra.
    /// </summary>
    Task<ResumenCartera> ObtenerCarteraAsync(int analistaId, CancellationToken ct = default);

    /// <summary>
    /// FUN-19: cambia nombre, rol o estado. Lo que es nulo no se toca.
    /// <para>
    /// Dar de baja o sacar del rol Analista deja una bandeja sin dueño: por eso, en la misma
    /// transaccion, se reasigna su cartera, se resuelven sus transferencias, se quitan sus
    /// asignaciones de cuenta —con alerta, para que Jefatura decida— y se le suben las sesiones.
    /// </para>
    /// </summary>
    Task ActualizarAsync(
        int analistaId, string? nombre, RolAnalista? rol, bool? activo, int autorId,
        CancellationToken ct = default);
}

/// <summary>FUN-19: lo que un analista tiene encima hoy.</summary>
public sealed record ResumenCartera(int Conversaciones, int CuentasTitular, int CuentasRespaldo);

/// <summary>ARQ-11: si el analista sigue habilitado y con que version de sesion (V34).</summary>
public sealed record EstadoSeguridad(bool Activo, int Version);

/// <summary>
/// Señal de vida de los procesos en segundo plano (Sección 9.6.2). El Worker la escribe al cerrar
/// cada ciclo; el health de la Api la lee para saber si sigue trabajando.
/// </summary>
public interface ILatidoServicio
{
    /// <summary>Deja constancia de que el bucle acaba de completar un ciclo.</summary>
    Task RegistrarAsync(
        string servicio, TimeSpan tolerancia, string? detalle = null, CancellationToken ct = default);

    /// <summary>Último latido de cada bucle vigilado.</summary>
    Task<IReadOnlyList<LatidoServicio>> ListarAsync(CancellationToken ct = default);
}

/// <summary>Lo que devuelve un intento de inicio de sesión.</summary>
/// <param name="VersionSeguridad">ARQ-11 (V34): viaja en el token; un token de una versión anterior ya no entra.</param>
public sealed record ResultadoAutenticacion(
    bool Exito, string? Motivo, int AnalistaId, string? Nombre, string? Rol, int VersionSeguridad = 0);

/// <summary>
/// Autenticación de analistas (Sección 9.6.1). Sin esto la Regla 4 —cada analista ve solo lo
/// suyo— no tiene efecto real: cualquiera que alcance la Api leería conversaciones ajenas.
/// </summary>
public interface IAutenticacionService
{
    Task<ResultadoAutenticacion> VerificarAsync(
        string email, string contrasena, CancellationToken ct = default);

    /// <summary>Fija o cambia la contraseña de un analista.</summary>
    Task EstablecerContrasenaAsync(int analistaId, string contrasena, CancellationToken ct = default);

    /// <summary>
    /// Si ningún analista tiene contraseña todavía. Es lo que habilita el arranque inicial: una vez
    /// que existe la primera, esa puerta se cierra sola y para siempre.
    /// </summary>
    Task<bool> SinContrasenasAsync(CancellationToken ct = default);
}
/// <summary>Catalogo de cuentas y vacantes, que es lo que alimenta el menu del bot.</summary>
public interface ICuentaService
{
    /// <summary>
    /// Lo unico que el menu del bot puede ofrecer (COR-09, AL5): cuentas activas, con titular activo y
    /// con al menos una vacante abierta que tenga formulario cargado.
    /// <para>
    /// Las tres condiciones son la misma: que elegir esa opcion lleve a alguna parte. Sin titular el
    /// hilo queda sin dueño, sin formulario no hay enlace que mandar y sin vacante no hay a que
    /// postular (Regla 20).
    /// </para>
    /// </summary>
    Task<IReadOnlyList<Cuenta>> ListarMenuAsync(CancellationToken ct = default);

    /// <summary>
    /// FUN-02 (A6): la vacante cuyo codigo de aviso aparece en el mensaje del postulante. Recibe todos
    /// los candidatos del texto porque el codigo viaja en una frase («Hola, postulo a K7M2QX») y cual
    /// de esas palabras es el codigo lo sabe la base, no quien la llama.
    /// <para>Devuelve la vacante aunque este cerrada: que lo este es informacion para la Regla 20.</para>
    /// </summary>
    Task<Hc?> BuscarVacantePorCodigoAsync(
        IReadOnlyCollection<string> candidatos, CancellationToken ct = default);

    /// <summary>
    /// FUN-20: corrige lo que se cargo mal en una vacante. Lo que viene nulo no se toca. La URL tiene que
    /// ser https —el enlace viaja en un mensaje y por el se manda el DNI y el CV— y el codigo, unico y
    /// transcribible.
    /// </summary>
    Task ActualizarVacanteAsync(
        int hcId, string? titulo, string? urlJobForms, string? codigoAviso, int analistaId,
        CancellationToken ct = default);

    /// <summary>FUN-20: una vacante cerrada por error vuelve a estar abierta, con su rastro.</summary>
    Task ReabrirVacanteAsync(int hcId, int analistaId, CancellationToken ct = default);

    /// <summary>
    /// FUN-20: las vacantes de una cuenta para administrarlas. Con <paramref name="incluirCerradas"/>
    /// tambien las cerradas, que es la unica forma de encontrar la que se cerro por error.
    /// </summary>
    Task<IReadOnlyList<Hc>> ListarVacantesDeCuentaAsync(
        int cuentaId, bool incluirCerradas, CancellationToken ct = default);

    /// <summary>
    /// FUN-20: corrige el nombre de una cuenta o la desactiva. Desactivarla la saca del menu del bot, pero
    /// no mueve lo que ya esta en curso: si quedan conversaciones abiertas, deja una alerta para que
    /// alguien las cierre (ARQ-09).
    /// </summary>
    Task ActualizarCuentaAsync(
        int cuentaId, string? nombre, bool? activo, int analistaId, CancellationToken ct = default);

    /// <summary>
    /// FUN-02: la cuenta del menu cuyo nombre normalizado coincide exactamente con el texto. Solo las
    /// del menu (<see cref="ListarMenuAsync"/>): ofrecer una cuenta sin titular o sin formulario por
    /// escrito tendria el mismo callejon sin salida que ofrecerla en el menu.
    /// </summary>
    Task<Cuenta?> BuscarCuentaDeMenuPorNombreAsync(
        string textoNormalizado, CancellationToken ct = default);

    Task<Cuenta?> ObtenerPorIdAsync(int cuentaId, CancellationToken ct = default);

    Task<IReadOnlyList<Hc>> ListarVacantesAbiertasAsync(int cuentaId, CancellationToken ct = default);

    /// <summary>Una vacante por id, abierta o cerrada. Nula si no existe.</summary>
    Task<Hc?> ObtenerVacanteAsync(int hcId, CancellationToken ct = default);

    /// <summary>
    /// Alta de vacante. Sin esto el estado de un HC nunca cambiaria y la Regla 20 no tendria
    /// como activarse en la practica.
    /// </summary>
    Task<Hc> CrearVacanteAsync(
        int cuentaId, string titulo, string? urlJobForms, int analistaId, CancellationToken ct = default);

    /// <summary>Regla 20: cerrar la vacante desactiva su enlace y hace que el bot deje de ofrecerla.</summary>
    Task CerrarVacanteAsync(int hcId, int analistaId, CancellationToken ct = default);

    /// <summary>
    /// Campos opcionales configurados para una vacante. Es lo que el JobForms consulta para saber
    /// que preguntar de mas, ademas de los campos fijos (Seccion 9.2).
    /// </summary>
    Task<IReadOnlyList<HcCampoOpcional>> ListarCamposOpcionalesAsync(
        int hcId, CancellationToken ct = default);

    /// <summary>
    /// Reemplaza el catalogo de campos opcionales de una vacante.
    /// <para>
    /// Es reemplazo y no edicion campo por campo porque el analista los define como un conjunto al
    /// abrir la vacante; ir agregando de a uno deja el formulario en estados intermedios que nadie
    /// configuro.
    /// </para>
    /// </summary>
    Task ReemplazarCamposOpcionalesAsync(
        int hcId, IReadOnlyList<HcCampoOpcional> campos, int analistaId, CancellationToken ct = default);

    /// <summary>
    /// Cuentas asignadas a un analista y en cuales es respaldo. Es lo que la bandeja usa para
    /// agrupar por subdivision (Seccion 7) sin tocar SQL Server.
    /// </summary>
    Task<IReadOnlyList<(Cuenta Cuenta, bool EsBackup, int VacantesAbiertas)>> ListarDeAnalistaAsync(
        int analistaId, CancellationToken ct = default);

    /// <summary>
    /// Regla 4 por cuenta: el titular y el respaldo trabajan lo de su cuenta —el tablero, las
    /// tarjetas—; Sistemas lo ve sin actuar; el resto no lo ve.
    /// </summary>
    Task<NivelAcceso> ObtenerAccesoAsync(int cuentaId, int analistaId, CancellationToken ct = default);

    /// <summary>Alta de cliente. El nombre es unico: es como el analista lo reconoce en la bandeja.</summary>
    Task<Cuenta> CrearAsync(string nombre, CancellationToken ct = default);

    /// <summary>Catalogo completo, inactivas incluidas: la administracion necesita verlas todas.</summary>
    Task<IReadOnlyList<Cuenta>> ListarTodasAsync(CancellationToken ct = default);

    /// <summary>
    /// Cada cuenta con su titular, su respaldo y cuantas vacantes tiene abiertas. Es la vista que
    /// hace falta para saber si la operacion esta lista: una cuenta sin titular no enruta a nadie.
    /// </summary>
    Task<IReadOnlyList<(Cuenta Cuenta, Analista? Titular, Analista? Respaldo, int VacantesAbiertas)>>
        ListarConDotacionAsync(CancellationToken ct = default);

    /// <summary>
    /// Regla 1 y Regla 2: pone al analista como titular o como respaldo de la cuenta. Cada cuenta
    /// admite un titular y un respaldo, y no pueden ser la misma persona: si lo fueran, escalar
    /// devolveria la conversacion a quien ya no respondio.
    /// </summary>
    Task AsignarAnalistaAsync(
        int cuentaId, int analistaId, bool esBackup, CancellationToken ct = default);

    Task QuitarAnalistaAsync(int cuentaId, int analistaId, CancellationToken ct = default);
}

/// <summary>Unico punto de acceso a la tabla de Mensajes.</summary>
public interface IMensajeService
{
    /// <summary>
    /// Persiste un mensaje entrante. Devuelve <c>null</c> si el <c>ProviderMessageId</c> ya existia:
    /// Meta reintenta la entrega y el mismo mensaje no puede procesarse dos veces (Seccion 9.6.2).
    /// </summary>
    Task<Mensaje?> RegistrarEntranteAsync(
        int conversacionId, MensajeEntranteDto dto, Guid correlationId, CancellationToken ct = default);

    /// <summary>
    /// V33: deja registrado el archivo de un entrante, <c>Pendiente</c> de descarga. Va en la misma
    /// transaccion que el mensaje: si no, una reentrega descartaria el mensaje por duplicado y el
    /// archivo no se registraria nunca.
    /// </summary>
    Task<MensajeAdjunto> RegistrarAdjuntoAsync(long mensajeId, MedioEntranteDto medio, CancellationToken ct = default);

    /// <summary>
    /// V33: los adjuntos por bajar a los que ya les toca, del mas viejo al mas nuevo: el id de medio
    /// caduca, y el que llego primero es el que menos tiempo tiene.
    /// </summary>
    Task<IReadOnlyList<MensajeAdjunto>> ListarAdjuntosPendientesAsync(
        DateTime ahoraUtc, int maximo, CancellationToken ct = default);

    /// <summary>
    /// V33: el archivo quedo guardado y escaneado. Devuelve false si el adjunto ya no existe —se borro
    /// con su mensaje mientras bajaba—, para que quien lo guardo no deje el archivo huerfano.
    /// </summary>
    Task<bool> MarcarAdjuntoDescargadoAsync(
        long adjuntoId, string ruta, long tamanoBytes, CancellationToken ct = default);

    /// <summary>
    /// V33: un intento que no llego a guardar el archivo. Con <paramref name="proximoIntentoUtc"/> se
    /// vuelve a intentar a esa hora; nulo significa que no se intenta mas y el adjunto queda rechazado.
    /// </summary>
    Task RegistrarFalloDescargaAsync(
        long adjuntoId, string error, DateTime? proximoIntentoUtc, CancellationToken ct = default);

    /// <summary>
    /// FUN-14: el adjunto, solo si es de esa conversacion. El acceso se decide por la conversacion de la
    /// ruta (Regla 4): sin esta condicion, cualquiera con acceso a un hilo podria pedir el archivo de otro
    /// cambiando el id.
    /// </summary>
    Task<MensajeAdjunto?> ObtenerAdjuntoAsync(long adjuntoId, int conversacionId, CancellationToken ct = default);

    /// <summary>
    /// Regla 17 con el criterio de A5 (V33): los adjuntos guardados de personas cuya ultima actividad
    /// —la del hilo y la de sus postulaciones— supera el plazo, y que no tienen un proceso vivo ni una
    /// contratacion.
    /// </summary>
    Task<IReadOnlyList<MensajeAdjunto>> ListarAdjuntosPorPurgarAsync(
        int diasRetencion, int maximo, CancellationToken ct = default);

    /// <summary>
    /// Regla 17: el archivo ya se borro. La fila queda como rastro, sin ruta, y la purga se audita como la
    /// del CV.
    /// </summary>
    Task MarcarAdjuntoPurgadoAsync(long adjuntoId, CancellationToken ct = default);

    /// <summary>
    /// FUN-16: vacia el contenido de los mensajes de esas conversaciones y da sus adjuntos por purgados.
    /// Devuelve las rutas de los archivos que hay que borrar: el que los guarda es quien puede borrarlos.
    /// </summary>
    Task<IReadOnlyList<string>> AnonimizarPorConversacionAsync(
        IReadOnlyCollection<int> conversacionIds, CancellationToken ct = default);

    /// <summary>
    /// Deja constancia de un mensaje que se envio, para que la bandeja muestre el hilo completo.
    /// <paramref name="parametrosPlantilla"/> se guarda para que un reintento pueda rearmar la
    /// plantilla: el contenido almacenado conserva los {{n}} sin reemplazar.
    /// </summary>
    Task<Mensaje> RegistrarSalienteAsync(
        int conversacionId, string contenido, int? plantillaId, int? analistaId,
        string? providerMessageId, Guid correlationId,
        IReadOnlyList<string>? parametrosPlantilla = null, CancellationToken ct = default);

    /// <summary>
    /// Aplica un acuse de entrega. Devuelve <c>null</c> si el mensaje no es nuestro: Meta acusa tambien
    /// lo que se manda desde el panel del proveedor.
    /// </summary>
    Task<ResultadoAcuse?> ActualizarEstadoEntregaAsync(EstadoEntregaDto dto, CancellationToken ct = default);

    /// <summary>
    /// Marca un saliente que nunca llego a salir. Va por <c>MensajeId</c> y no por el id del
    /// proveedor porque justamente no hay: un envio rechazado no devuelve ninguno, y buscarlo por
    /// un id inventado deja el fallo sin registrar en la base (Seccion 9.6.2).
    /// <para>
    /// <paramref name="proximoIntentoUtc"/> nulo significa que no se va a reintentar: o el fallo
    /// es permanente, o es ambiguo, o se agotaron los intentos.
    /// </para>
    /// </summary>
    Task MarcarEnvioFallidoAsync(
        long mensajeId, string? error, ClaseFallo clase = ClaseFallo.Permanente,
        DateTime? proximoIntentoUtc = null, CancellationToken ct = default);

    /// <summary>
    /// Salientes que fallaron por causa transitoria y a los que ya les toca otro intento.
    /// Devuelve la conversacion incluida porque el reintento tiene que revalidar la Regla 15 antes
    /// de volver a enviar.
    /// </summary>
    Task<IReadOnlyList<Mensaje>> ListarPendientesDeReintentoAsync(
        int maximo, DateTime ahoraUtc, CancellationToken ct = default);

    /// <summary>Un reintento que salio bien: queda el id del proveedor y no vuelve a intentarse.</summary>
    Task MarcarEnvioLogradoAsync(long mensajeId, string? providerMessageId, CancellationToken ct = default);

    Task<IReadOnlyList<Mensaje>> ListarPorConversacionAsync(
        int conversacionId, int maximo = 100, CancellationToken ct = default);

    /// <summary>
    /// Registra un saliente decidido sin enviarlo (V29). Devuelve <c>null</c> si la clave ya existe:
    /// es el mismo envio decidido otra vez (evento reprocesado, doble clic) y no debe duplicarse.
    /// <para>
    /// <paramref name="reservadoParaEnvio"/> lo usa la respuesta del analista, que envia en linea
    /// (V14): la fila nace <c>Enviando</c> y el despachador del Worker nunca la toma. Si naciera
    /// <c>EnCola</c>, podria tomarla entre el guardado y el envio en linea y el mensaje saldria dos
    /// veces (V29).
    /// </para>
    /// </summary>
    Task<Mensaje?> EncolarSalienteAsync(
        int conversacionId, SalienteEncolado saliente, string claveIdempotencia, int? analistaId,
        Guid correlationId, bool reservadoParaEnvio = false, CancellationToken ct = default);

    Task<Mensaje?> ObtenerPorClaveIdempotenciaAsync(string claveIdempotencia, CancellationToken ct = default);

    /// <summary>Toma los <c>EnCola</c> mas antiguos y los pasa a <c>Enviando</c>. Hay un solo Worker (V24).</summary>
    Task<IReadOnlyList<Mensaje>> TomarLoteEnColaAsync(int maximo, CancellationToken ct = default);

    /// <summary>
    /// Los <c>Enviando</c> mas viejos que <paramref name="antiguedad"/> son envios cuyo resultado se
    /// perdio (el proceso murio a mitad): pasan a Fallido/Ambiguo sin proximo intento. Devuelve cuantos.
    /// </summary>
    Task<int> RecuperarEnviandoVencidosAsync(TimeSpan antiguedad, CancellationToken ct = default);
}

/// <summary>
/// Lo que hace falta para despachar un saliente sin volver a evaluar las reglas (V29): el tipo, el
/// contenido a mostrar en el hilo y lo necesario para rearmarlo ante el proveedor.
/// </summary>
public sealed record SalienteEncolado(
    TipoSaliente Tipo,
    string Contenido,
    int? PlantillaId = null,
    IReadOnlyList<string>? Parametros = null,
    IReadOnlyList<BotonRespuesta>? Opciones = null,
    string? TextoBotonLista = null);

/// <summary>Forma serializada en <c>Mensaje.OpcionesJson</c>. La escribe la cola y la lee el despachador.</summary>
public sealed record OpcionesSaliente(IReadOnlyList<BotonRespuesta> Opciones, string? TextoBotonLista);

/// <summary>
/// En que quedo un acuse de entrega (FUN-13).
/// <para>
/// <c>AnalistaId</c> es a quien avisar si el mensaje no llego: su autor, o quien atiende el hilo si lo
/// mando el bot; nulo si no hay nadie. <c>PasoAFallido</c> es true solo en la transicion, para que
/// una reentrega del mismo acuse no repita el aviso (P4).
/// </para>
/// </summary>
public sealed record ResultadoAcuse(long MensajeId, int ConversacionId, int? AnalistaId, bool PasoAFallido);

/// <summary>
/// En que quedo un movimiento del tablero (COR-11). <c>Aplicado</c> es false cuando otro analista
/// movio la misma tarjeta primero: gana el suyo y este movimiento no cambio nada.
/// </summary>
public sealed record ResultadoMovimiento(
    EstadoPostulacion Anterior, EstadoPostulacion Nuevo, bool Aplicado);

/// <summary>
/// Postulaciones: la persona aplicando a una vacante concreta. Es lo que recorre el tablero kanban.
/// Se separo de <see cref="IConversacionService"/> porque el kanban es por vacante (Regla 13) y una
/// misma conversacion de WhatsApp puede sostener varias postulaciones (Regla 6).
/// </summary>
public interface IPostulacionService
{
    Task<Postulacion> CrearAsync(int postulanteId, int hcId, CancellationToken ct = default);

    /// <summary>
    /// Regla 13: mueve la postulacion entre columnas del tablero. La columna decide el desenlace por
    /// su <see cref="EtapaKanban.EstadoResultante"/> (COR-11), no por su nombre.
    /// </summary>
    Task<ResultadoMovimiento> MoverEtapaKanbanAsync(
        int postulacionId, int etapaId, int analistaId, CancellationToken ct = default);

    /// <summary>
    /// Regla 7: whitelist (motivo opcional) o blacklist (motivo obligatorio).
    /// <para>
    /// Devuelve las postulaciones que quedaron descartadas por la marca, que son las que
    /// disparan el cierre de cortesia de la Regla 12.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<int>> MarcarEstadoAsync(
        int postulanteId, int cuentaId, TipoEstadoPostulante tipo,
        string? motivo, int analistaId, CancellationToken ct = default);

    /// <summary>Postulaciones de una persona, para mostrar su historial junto al chat.</summary>
    Task<IReadOnlyList<Postulacion>> ObtenerTableroPorPostulanteAsync(
        int postulanteId, CancellationToken ct = default);

    /// <summary>Una postulacion por id, sin seguimiento. Nula si no existe.</summary>
    Task<Postulacion?> ObtenerPorIdAsync(int postulacionId, CancellationToken ct = default);

    /// <summary>Desenlace actual de una postulacion. Nulo si no existe.</summary>
    Task<EstadoPostulacion?> ObtenerEstadoAsync(int postulacionId, CancellationToken ct = default);

    /// <summary>Tablero completo de una vacante, agrupado por etapa.</summary>
    Task<IReadOnlyList<Postulacion>> ObtenerTableroAsync(int hcId, CancellationToken ct = default);

    /// <summary>
    /// Columnas del tablero, en orden. El tablero las necesita completas y no solo las que hoy
    /// tienen tarjetas: una etapa vacia sigue siendo una columna a la que se puede arrastrar.
    /// </summary>
    Task<IReadOnlyList<EtapaKanban>> ListarEtapasAsync(CancellationToken ct = default);

    /// <summary>Regla 6: otras cuentas en las que el mismo DNI esta en proceso, para el aviso generico.</summary>
    Task<IReadOnlyList<Cuenta>> ObtenerOtrasCuentasEnProcesoAsync(
        int postulanteId, int cuentaExcluidaId, CancellationToken ct = default);

    /// <summary>FUN-11 (A14): la postulacion se archiva por inactividad, con su rastro de auditoria.</summary>
    Task ArchivarAsync(int postulacionId, string motivo, CancellationToken ct = default);

    /// <summary>
    /// FUN-10 (A11): deja pedido —o no— el cierre de cortesia al descartar. No hace nada si ya salio:
    /// descartar dos veces a la misma persona no le manda dos mensajes de despedida (P4).
    /// </summary>
    Task MarcarCierrePendienteAsync(
        int postulacionId, bool enviar, bool automatico, CancellationToken ct = default);

    /// <summary>FUN-10: cierres pedidos que todavia no salieron, para que el barrido los mande en horario.</summary>
    Task<IReadOnlyList<int>> ListarCierresPendientesAsync(int maximo, CancellationToken ct = default);

    /// <summary>
    /// FUN-11: postulaciones en curso o descartadas sin actividad desde hace <paramref name="dias"/>.
    /// Prefiltro del barrido: si corresponde archivar lo decide la Regla 16.
    /// </summary>
    Task<IReadOnlyList<int>> ListarPorArchivarAsync(int dias, int maximo, CancellationToken ct = default);

    /// <summary>A14: postulaciones en curso que entraron en la ventana de aviso previo al archivado.</summary>
    Task<IReadOnlyList<int>> ListarPorAvisarArchivadoAsync(
        int dias, int diasAviso, int maximo, CancellationToken ct = default);

    /// <summary>
    /// FUN-08 (A2): la persona vuelve a un proceso —ex trabajador, o un descarte que el analista
    /// reconsidera—. Cuenta como proceso vivo: no se archiva (R16), no se repregunta la empresa (R9)
    /// y sus mensajes van directo a su analista.
    /// </summary>
    Task MarcarReingresoAsync(int postulacionId, int analistaId, CancellationToken ct = default);

    /// <summary>FUN-10, FUN-11: sella un aviso o el cierre de cortesia para que salga una sola vez (P4).</summary>
    Task SellarAsync(int postulacionId, MarcaPostulacion marca, CancellationToken ct = default);
}

public interface IPostulanteService
{
    Task<Postulante?> BuscarPorDniAsync(string dni, CancellationToken ct = default);

    /// <summary>Crea el postulante o actualiza el que ya existe con ese DNI (Regla 9).</summary>
    Task<Postulante> RegistrarDesdeFormularioAsync(
        DatosPostulanteFormulario datos, CancellationToken ct = default);

    /// <summary>El flujo muestra el historial previo cuando el DNI ya existe.</summary>
    Task<IReadOnlyList<Postulacion>> ObtenerHistorialAsync(string dni, CancellationToken ct = default);

    /// <summary>
    /// Regla 17: borrado efectivo de datos personales ante una solicitud del postulante.
    /// Anonimiza en vez de eliminar filas, para no romper las metricas historicas de la Regla 18.
    /// </summary>
    Task AnonimizarDatosAsync(string dni, string motivo, CancellationToken ct = default);
}

public interface IJobFormsService
{
    Task<JobFormsRespuesta> ValidarEnvioAsync(JobFormsRespuesta respuesta, CancellationToken ct = default);

    Task<string> AlmacenarCvAsync(Stream contenido, string nombreArchivo, string contentType, CancellationToken ct = default);

    /// <summary>Regla 20: la vacante sigue abierta y su enlace es valido.</summary>
    Task<bool> ValidarVacanteActivaAsync(int hcId, CancellationToken ct = default);

    /// <summary>Regla 17: el postulante acepto el aviso de privacidad vigente.</summary>
    Task<bool> ValidarConsentimientoAsync(JobFormsRespuesta respuesta, CancellationToken ct = default);

    /// <summary>
    /// La respuesta ya guardada de esa invitacion, si existe. Es lo que permite reconocer un envio
    /// repetido sin volver a procesarlo (V27).
    /// </summary>
    Task<JobFormsRespuesta?> ObtenerRespuestaDeInvitacionAsync(int invitacionId, CancellationToken ct = default);

    /// <summary>
    /// Regla 17: respuestas cuyo CV ya cumplio el plazo de retencion. Es lo que consume el job de
    /// purga del Worker (Seccion 9.6.5).
    /// </summary>
    Task<IReadOnlyList<JobFormsRespuesta>> ListarCvsPorPurgarAsync(
        int diasRetencion, int maximo, CancellationToken ct = default);

    /// <summary>Borra el CV y limpia su referencia, dejando constancia en la auditoria.</summary>
    Task PurgarCvAsync(int respuestaId, CancellationToken ct = default);

    /// <summary>
    /// Borra un CV ya guardado que no quedo asociado a ninguna fila: el camino propio de
    /// <c>Enviar</c> (COR-15/M8) lo guarda antes de saber si <c>recepcion.ProcesarAsync</c> va a
    /// aceptar el envio, y si no lo acepta el archivo queda huerfano en el recurso compartido
    /// (invariante 9). A diferencia de <see cref="PurgarCvAsync"/>, no toca ninguna fila: no hay
    /// respuesta que limpiar porque nunca llego a guardarse.
    /// </summary>
    Task EliminarCvAsync(string ruta, CancellationToken ct = default);
}

/// <summary>
/// Seguimiento del link de JobForms. Es lo que el Worker consulta para disparar el recordatorio
/// de 24h y el aviso al analista de 48h (Regla 9).
/// </summary>
public interface IJobFormsInvitacionService
{
    Task<JobFormsInvitacion> CrearInvitacionAsync(int conversacionId, int hcId, CancellationToken ct = default);

    Task<JobFormsInvitacion?> ObtenerPorTokenAsync(Guid token, CancellationToken ct = default);

    Task MarcarRecordatorioEnviadoAsync(int invitacionId, CancellationToken ct = default);

    Task MarcarAvisoAnalistaEnviadoAsync(int invitacionId, CancellationToken ct = default);

    Task MarcarCompletadoAsync(int invitacionId, CancellationToken ct = default);

    Task<IReadOnlyList<JobFormsInvitacion>> ListarPendientesRecordatorioAsync(TimeSpan antiguedad, CancellationToken ct = default);

    Task<IReadOnlyList<JobFormsInvitacion>> ListarPendientesAvisoAnalistaAsync(TimeSpan antiguedad, CancellationToken ct = default);
}

/// <summary>
/// Resuelve que plantilla aprobada por Meta usar y si la ventana de 24h sigue abierta.
/// Sin esta pieza, las Reglas 3, 9, 12 y 15 no tienen quien las ejecute.
/// </summary>
public interface IPlantillaService
{
    Task<Plantilla?> ObtenerPlantillaParaEventoAsync(string clave, CancellationToken ct = default);

    /// <summary>
    /// La plantilla tal como esta ahora, activa o no. La usa quien revalida un envio ya decidido
    /// (V29): tiene que poder distinguir "desactivada" de "no existe".
    /// </summary>
    Task<Plantilla?> ObtenerPorIdAsync(int plantillaId, CancellationToken ct = default);

    /// <summary>true si se puede enviar texto libre; false si el envio debe ir como plantilla.</summary>
    Task<bool> ValidarVentana24hAsync(int conversacionId, CancellationToken ct = default);

    Task<IReadOnlyList<Plantilla>> ListarActivasAsync(CancellationToken ct = default);

    /// <summary>Catalogo completo, incluidas las inactivas: soporte necesita ver que falta aprobar.</summary>
    Task<IReadOnlyList<Plantilla>> ListarTodasAsync(CancellationToken ct = default);
}

/// <summary>Regla 14: se consulta antes de asignar una conversacion.</summary>
public interface IAusenciaService
{
    Task<Ausencia> RegistrarAsync(int analistaId, DateTime inicio, DateTime fin, string? motivo, CancellationToken ct = default);

    Task<bool> EstaAusenteAsync(int analistaId, DateTime momento, CancellationToken ct = default);

    /// <summary>Las que terminan despues de <paramref name="desdeUtc"/>: en curso y programadas.</summary>
    Task<IReadOnlyList<Ausencia>> ListarVigentesAsync(int analistaId, DateTime desdeUtc, CancellationToken ct = default);

    /// <summary>Cancela una ausencia de ese analista. False si no existe o es de otro.</summary>
    Task<bool> EliminarAsync(int analistaId, int ausenciaId, CancellationToken ct = default);

    /// <summary>
    /// FUN-12 (A10): ausencias que ya terminaron y cuyo titular todavia no recibio el resumen de lo
    /// que quedo con su respaldo.
    /// </summary>
    Task<IReadOnlyList<Ausencia>> ListarFinalizadasSinAvisoAsync(DateTime ahora, CancellationToken ct = default);

    /// <summary>FUN-12: sella el aviso de retorno para que salga una sola vez por ausencia (P4).</summary>
    Task MarcarAvisoRetornoAsync(int ausenciaId, CancellationToken ct = default);
}

/// <summary>Regla 3: valida si un mensaje entrante llega dentro del horario laboral configurado.</summary>
public interface IHorarioAtencionService
{
    Task<bool> EstaEnHorarioAsync(int? cuentaId, DateTime momentoUtc, CancellationToken ct = default);

    /// <summary>Texto del horario vigente, para el parametro de la plantilla de fuera de horario.</summary>
    Task<string> DescribirHorarioAsync(int? cuentaId, CancellationToken ct = default);

    /// <summary>
    /// Minutos de horario laboral transcurridos entre dos instantes. Es lo que permite que el
    /// escalamiento de la Regla 2 no corra de madrugada: el calendario se resuelve aca y la
    /// decision de que hacer con el numero se queda en la regla.
    /// </summary>
    Task<double> MinutosHabilesEntreAsync(
        int? cuentaId, DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct = default);

    /// <summary>
    /// El ultimo cierre anterior a <paramref name="momentoUtc"/>, si cae fuera de horario (V31). Es la
    /// marca que permite un solo aviso fuera de horario por periodo (A8). Nulo dentro de horario o sin tramos.
    /// </summary>
    Task<DateTime?> InicioPeriodoFueraDeHorarioAsync(int? cuentaId, DateTime momentoUtc, CancellationToken ct = default);

    /// <summary>La proxima apertura despues de <paramref name="momentoUtc"/>, para decirle al postulante cuando se retoma (A8).</summary>
    Task<DateTime?> ProximaAperturaAsync(int? cuentaId, DateTime momentoUtc, CancellationToken ct = default);

    /// <summary>
    /// FUN-07 (A1): cuando se cumplen esos minutos habiles a partir de <paramref name="desdeUtc"/>. Es
    /// lo que fija el vencimiento de una transferencia pedida sobre el cierre de la jornada.
    /// </summary>
    Task<DateTime> SumarMinutosHabilesAsync(
        int? cuentaId, DateTime desdeUtc, double minutos, CancellationToken ct = default);

    /// <summary>Tramos configurados para una cuenta, o los generales si <paramref name="cuentaId"/> es nulo.</summary>
    Task<IReadOnlyList<HorarioAtencion>> ObtenerTramosAsync(int? cuentaId, CancellationToken ct = default);

    /// <summary>
    /// Reemplaza el horario completo de una cuenta. Es reemplazo y no edicion parcial porque el
    /// horario se piensa como una semana entera: dejar tramos viejos mezclados con nuevos produce
    /// jornadas que nadie configuro.
    /// </summary>
    Task ReemplazarTramosAsync(
        int? cuentaId, IReadOnlyList<HorarioAtencion> tramos, int analistaId, CancellationToken ct = default);
}

/// <summary>
/// Almacenamiento de CVs fuera de SQL Server (Seccion 8.3). En el despliegue on-premise la
/// implementacion escribe en un recurso compartido; la interfaz deja la puerta abierta a
/// mover los archivos a almacenamiento de objetos sin tocar el resto del sistema.
/// </summary>
public interface IAlmacenamientoCv
{
    Task<string> GuardarAsync(Stream contenido, string nombreArchivo, string contentType, CancellationToken ct = default);

    Task<Stream?> ObtenerAsync(string ruta, CancellationToken ct = default);

    Task EliminarAsync(string ruta, CancellationToken ct = default);
}

/// <summary>
/// Los archivos que manda el postulante por WhatsApp (V33): el mismo circuito que el CV —cuarentena,
/// antivirus, tope y extensiones permitidas— en su propia carpeta y con sus propios limites, porque
/// por WhatsApp llegan tambien fotos, audios y videos.
/// </summary>
public interface IAlmacenamientoAdjuntos
{
    /// <summary>
    /// Guarda y devuelve donde quedo. <paramref name="nombreArchivo"/> solo lo traen los documentos: sin
    /// el, la extension sale del tipo. Un rechazo en firme es <c>ArchivoRechazadoException</c>;
    /// cualquier otra excepcion es un fallo que se puede reintentar.
    /// </summary>
    Task<ArchivoGuardado> GuardarAsync(
        Stream contenido, string? nombreArchivo, string mimeType, CancellationToken ct = default);

    Task<Stream?> ObtenerAsync(string ruta, CancellationToken ct = default);

    Task EliminarAsync(string ruta, CancellationToken ct = default);
}

/// <summary>Ruta relativa a la carpeta configurada, y lo que realmente se escribio.</summary>
public sealed record ArchivoGuardado(string Ruta, long TamanoBytes);

/// <summary>Qué dijo el antivirus sobre un archivo.</summary>
public enum ResultadoEscaneo
{
    /// <summary>El escaneo corrió y no encontró nada.</summary>
    Limpio = 1,

    /// <summary>El escaneo corrió y encontró algo.</summary>
    Amenaza = 2,

    /// <summary>
    /// No se pudo escanear: el antivirus está apagado, no está instalado o no respondió a tiempo.
    /// Se distingue de <see cref="Limpio"/> a propósito — "no sé" no es "está bien", y quien
    /// llama decide si eso alcanza para aceptar el archivo.
    /// </summary>
    NoDisponible = 3
}

public sealed record VeredictoEscaneo(ResultadoEscaneo Resultado, string? Detalle);

/// <summary>
/// Escaneo antivirus de los adjuntos (Sección 9.6.1). El endpoint del JobForms es el único
/// alcanzable desde internet sin autenticación, así que es por donde entraría un archivo hostil.
/// </summary>
public interface IEscanerAntivirus
{
    Task<VeredictoEscaneo> EscanearAsync(string rutaArchivo, CancellationToken ct = default);
}

/// <summary>Lee la configuracion parametrizable de las reglas, sin obligar a redeploy para ajustarla.</summary>
public interface IConfiguracionReglasService
{
    Task<IReadOnlyDictionary<string, string>> ObtenerTodasAsync(CancellationToken ct = default);

    /// <summary>Los parametros con su descripcion, para administrarlos. Sin cache: se pide rara vez.</summary>
    Task<IReadOnlyList<ConfiguracionRegla>> ListarAsync(CancellationToken ct = default);

    Task EstablecerAsync(string clave, string valor, CancellationToken ct = default);
}

/// <summary>
/// Trazabilidad de cambios (Regla 4). Se registra desde un solo lugar para que el formato de las
/// entradas sea consistente y la auditoria se pueda leer.
/// </summary>
public interface IAuditoriaService
{
    Task RegistrarAsync(
        string entidadTipo, string entidadId, int? analistaId,
        string accion, string? detalle, CancellationToken ct = default);

    Task<IReadOnlyList<Auditoria>> ListarPorEntidadAsync(
        string entidadTipo, string entidadId, CancellationToken ct = default);

    /// <summary>
    /// FUN-16: vacia el detalle de la auditoria de esas conversaciones y de ese postulante. Que paso y
    /// cuando se conserva —es lo que sostiene la trazabilidad de la Regla 4—; el detalle es donde se
    /// cuelan el nombre, el telefono y lo que escribio la persona.
    /// </summary>
    Task AnonimizarDetallesAsync(
        IReadOnlyCollection<int> conversacionIds, int postulanteId, CancellationToken ct = default);
}

/// <summary>Outbox. Los efectos secundarios se publican aca y el Worker los consume.</summary>
public interface IEventoSistemaService
{
    Task PublicarAsync(string tipo, object payload, Guid correlationId, CancellationToken ct = default);

    /// <summary>
    /// Pendientes de los <paramref name="tipos"/> indicados, del mas antiguo al mas nuevo.
    /// <para>
    /// El filtro por tipo no es comodidad: la outbox tiene varios consumidores previstos y no todos
    /// existen todavia. Sin el, los eventos que aun no tienen duenno se acumularian a la cabeza de
    /// la cola y taparian a los que si se pueden procesar. Una lista vacia devuelve todos los tipos.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<EventoSistema>> ObtenerPendientesAsync(
        int maximo, IReadOnlyCollection<string> tipos, CancellationToken ct = default);

    Task MarcarProcesadoAsync(long eventoId, CancellationToken ct = default);

    Task MarcarFallidoAsync(long eventoId, string error, int reintentosMaximos, CancellationToken ct = default);

    /// <summary>
    /// ARQ-13: borra los eventos ya procesados mas viejos que <paramref name="dias"/> y devuelve cuantos.
    /// Lo pendiente y lo fallido no se toca: uno espera consumidor y el otro espera a una persona.
    /// <para>
    /// Borra de a <paramref name="tamanoLote"/> filas, tantas vueltas como haga falta: una sola
    /// sentencia sobre meses de historico bloquearia la tabla mientras el webhook sigue publicando.
    /// </para>
    /// </summary>
    Task<int> PurgarProcesadosAsync(int dias, int tamanoLote, CancellationToken ct = default);

    /// <summary>
    /// FUN-16: vacia el payload de los eventos que referencian esas conversaciones. Con ARQ-13 el
    /// payload ya no trae datos personales, pero hay historico anterior, y un pedido de eliminacion no
    /// puede dejarlo vivo en una tabla que nadie mira.
    /// </summary>
    Task<int> AnonimizarPorConversacionAsync(
        IReadOnlyCollection<int> conversacionIds, CancellationToken ct = default);
}
