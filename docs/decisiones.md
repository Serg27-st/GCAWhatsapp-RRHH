# Decisiones de diseño

Registro de lo que se decidió al pasar del dossier al código, y en qué se apartó de él y por qué.
La fuente de requisitos sigue siendo `Dossier_Maestro_WhatsApp_RRHH_v2.docx`.

## Decisiones tomadas con la gerencia / Sistemas

| # | Tema | Decisión | Impacto |
|---|---|---|---|
| D1 | Camino de implementación | **Camino B** — construcción propia (Sección 9 del dossier) | Se desarrolla la solución .NET completa |
| D2 | Proveedor de WhatsApp API | **360dialog (BSP)** | `IWhatsAppProvider` se implementa primero contra 360dialog |
| D3 | JobForms | **Google Forms primero**, migrar a Razor Pages después | Reglas 17 y 20 quedan parcialmente manuales (ver Riesgos) |
| D4 | Modelo de datos | **Agregar tabla `Postulaciones`** | El kanban pasa a ser por vacante, no por cuenta |
| D5 | Despliegue | **On-premise** (IIS + SQL Server de la empresa) | CVs en recurso compartido, secretos en variables de entorno |

## Desviaciones respecto del dossier

Cada una corrige un punto donde el diseño de la Sección 9 no sostenía las reglas ya acordadas.

### V1 — `Conversaciones` se separa de `Postulaciones`

**Dossier:** `Conversaciones` lleva `EtapaKanbanId`, y la Regla 6 dice que un mismo DNI en dos
cuentas abre "conversaciones independientes".

**Problema:** la API de WhatsApp entrega los mensajes indexados por número de teléfono. Existe un
solo hilo físico entre el número de la empresa y el del postulante; no es posible tener dos. Y el
tablero kanban es por vacante (Sección 7 y Regla 13), pero `Conversaciones` no tenía `HCId`.

**Decisión:** dos entidades con responsabilidades distintas.

- `Conversacion` — el **canal**. Una por número de teléfono. Lleva el contexto de cuenta actual,
  el analista que atiende, el opt-in y las fechas que gobiernan la ventana de 24h.
- `Postulacion` — el **proceso**. Una por (postulante, vacante). Lleva la etapa del kanban, el
  analista asignado y el desenlace.

Así la Regla 6 se cumple de verdad: dos postulaciones independientes, cada una visible solo para
su analista, sobre un único hilo de WhatsApp.

**Pendiente de definir con RRHH:** cómo se presenta ese hilo único al postulante cuando dos
analistas de cuentas distintas le escriben. La recomendación es prefijar el mensaje saliente con
el nombre de la cuenta/vacante y que el bot repregunte la empresa cuando haya ambigüedad.

### V2 — `Conversaciones.PostulanteId` es nullable

Cuando alguien escribe por primera vez solo se conoce su teléfono; el DNI llega recién al
completar el JobForms. La clave natural del hilo es `TelefonoE164` (índice único), y el vínculo
con `Postulante` se establece después.

### V3 — El código de JobForms se mueve de `Cuentas` a `HC`

El dossier ponía `CódigoJobForms` en `Cuentas`, pero el flujo envía "el link del JobForms
correspondiente al HC" y `HCCamposOpcionales` confirma que las preguntas varían por vacante.

### V4 — Registro explícito de opt-in

La Regla 15 dice que el sistema impide el envío si no existe opt-in registrado, pero ninguna
tabla lo guardaba. Se agregaron `FechaOptIn` y `OrigenOptIn` a `Conversaciones` — a nivel de
número de teléfono, que es como Meta lo evalúa.

### V5 — El escalamiento de la Regla 2 respeta el horario de la Regla 3

Sin esto, un mensaje recibido al cierre de la jornada escala de madrugada hacia un respaldo que
tampoco está disponible. Se agregó el parámetro `escalamiento.solo_horario_laboral`
(por defecto `true`) en `ConfiguracionReglas`, ajustable sin redeploy.

### V6 — No se agrega una capa de repositorios sobre EF Core

El dossier menciona el patrón Repository. `DbContext` ya es un Unit of Work con repositorios por
`DbSet`, y envolverlo agrega ceremonia sin resolver nada. El objetivo real del dossier — que
ningún módulo dependa del esquema interno de otro — lo cumplen los servicios de dominio
(`IConversacionService`, `IPostulacionService`, …), que son la frontera de cada módulo.

### V7 — Proyecto `RRHH.WhatsApp.Contracts`

El dossier pide que el Frontend consuma únicamente la API por HTTP, sin referencia al dominio.
Sin un proyecto de DTOs compartidos eso obliga a duplicar a mano cada contrato. `Contracts` no
tiene dependencias y lo referencian solo `Api` y `Frontend`, así que la regla se mantiene intacta.

### V8 — Las plantillas nacen inactivas

`Plantillas` viene sembrada con las 6 que las reglas necesitan, pero con `Activa = false`. Cada
una debe registrarse y aprobarse en Meta antes de activarse. Es deliberado: el objetivo del
proyecto es dejar de recibir bloqueos, y un envío con una plantilla no aprobada es exactamente lo
que los provoca.

### V9 — La outbox se consume filtrando por tipo de evento

**Dossier:** describe `EventosSistema` como tabla outbox con reintentos, sin decir quién la lee.

**Problema:** los eventos que publica el ejecutor tienen destinatarios distintos —
`MensajeEntranteRecibido` es del motor de reglas, `AnalistaNotificado` es de la bandeja por
SignalR, `EtapaKanbanSolicitada` es de `IPostulacionService`— y varios de esos consumidores todavía
no existen. Con una sola columna `Estado` y una consulta que toma los N más antiguos, los eventos
sin dueño se acumulan a la cabeza de la cola y tapan a los que sí se pueden procesar.

**Decisión:** `ObtenerPendientesAsync` recibe los tipos que el consumidor sabe atender, y cada
consumidor declara los suyos (`ProcesadorOutbox.TiposQueAtiende`). Un evento sin dueño se queda
`Pendiente` — no se descarta ni se marca fallido — y lo recoge su consumidor el día que exista,
incluido el atraso acumulado.

**Alternativa descartada:** marcarlos `Procesado` al pasar. Deja la cola limpia, pero pierde en
silencio información que después hace falta (las notificaciones al analista, sobre todo).

### V10 — La Regla 9 se implementa como dos reglas

**Dossier:** la Regla 9 es una sola entrada que cubre el flujo completo de identificación por DNI:
menú del bot, envío del link, recordatorio a las 24h, aviso al analista a las 48h y repregunta de
empresa tras 3 días de inactividad.

**Problema:** esos pedazos no responden al mismo disparador. El recordatorio y el aviso los dispara
el paso del tiempo; la repregunta la dispara un mensaje entrante. Una sola clase tendría que
ramificar por `TipoDisparador` en el primer `if`, y su prueba unitaria dejaría de ser una prueba de
una regla para volverse una de varias.

**Decisión:** dos clases, `R09SeguimientoJobForms` (tiempo) y `R09RepreguntaEmpresa` (mensaje
entrante). Ambas declaran `Codigo = "R09"`, porque el código identifica la regla del dossier y no
la clase. Hay entonces más reglas registradas que códigos distintos, y eso es esperable.

**Queda fuera todavía:** el envío del link del JobForms, que es la primera mitad de la regla.
Depende del circuito completo —menú de vacantes cuando la cuenta tiene más de un HC, `IJobFormsService`
y el webhook de Google Apps Script— y se aborda con ese módulo, no con el Worker.


### V11 — `RegistrarDesdeFormulario` recibe los datos, no la respuesta

**Dossier:** `IPostulanteService.RegistrarDesdeFormulario(JobFormsRespuesta)`.

**Problema:** `JobFormsRespuesta` ya lleva `PostulanteId`. Para armarla hay que haber creado antes
a la persona que ese mismo método debería crear.

**Decisión:** el método recibe `DatosPostulanteFormulario` — DNI, nombre, teléfono, email — y
devuelve el `Postulante` creado o actualizado. El orden real del circuito queda: datos del
formulario → persona → respuesta → postulación.

### V12 — El envío del enlace es un efecto, no una decisión

**Problema:** la Regla 9 tiene que mandar el enlace del JobForms, pero armar la URL y crear la fila
de `JobFormsInvitaciones` son efectos con estado. Si los hiciera la regla, dejaría de poder
probarse sin base de datos.

**Decisión:** la regla devuelve `EnviarLinkJobForms(HcId)` y el ejecutor hace el resto: crea la
invitación, arma la URL con su token y manda el mensaje. La invitación nace ahí porque es la fila
que después sostiene el recordatorio de 24h y el aviso al analista de 48h.

**Consecuencia:** si la cuenta tiene más de una vacante abierta, la regla no puede elegir por el
postulante y devuelve `MostrarMenuVacantes`. El formulario es por HC, no por cliente.


### V13 — La blacklist descarta las postulaciones de esa cuenta

**Dossier:** la Regla 7 marca al postulante como descartado en `EstadosPostulanteCuenta`, y la
Regla 13 tiene una columna "Descartado" en el kanban. Son dos registros distintos del mismo hecho.

**Problema:** si marcar blacklist solo escribiera la marca, las postulaciones de esa persona en esa
cuenta seguirían `EnProceso` y apareciendo en el tablero. El analista vería una tarjeta viva de
alguien que él mismo acaba de descartar, y la Regla 12 no tendría de dónde saber que corresponde
el mensaje de cierre.

**Decisión:** marcar blacklist en una cuenta pasa a `Descartado` las postulaciones `EnProceso` de
esa persona en esa cuenta, y las mueve a la columna final del tablero. El desenlace queda en un
solo lugar —`Postulacion.Estado`— y tanto la blacklist como arrastrar la tarjeta a "Descartado"
disparan el mismo cierre de cortesía.

**Alcance:** solo esa cuenta. La Regla 6 es explícita en que las postulaciones de otras cuentas son
independientes, y un descarte en Alicorp no dice nada sobre el proceso en Intradevco.

### V14 — La respuesta del analista no pasa por la outbox

**Problema:** todo lo demás que envía mensajes se encola y lo ejecuta el Worker. La respuesta del
analista no puede: si la Regla 15 bloquea el envío —sin opt-in, o fuera de la ventana de 24h—, el
analista tiene que enterarse en el acto, no descubrirlo después en un log.

**Decisión:** `EnvioAnalista` corre síncrono dentro del request. Consulta al motor, respeta lo que
la Regla 15 decida y devuelve a la bandeja si el mensaje salió, y si no, por qué: `RequierePlantilla`
distingue el rechazo que se resuelve eligiendo una plantilla del que no tiene salida.

**Efecto secundario que importa:** es también el único lugar que marca
`FechaUltimaRespuestaAnalista`, que es lo que detiene el reloj del escalamiento de la Regla 2.
Hasta que existió este camino, nada la escribía.

### V15 — Reporting lee las tablas transaccionales, sin copia alimentada por eventos

**Dossier:** Sección 8.2 propone un modelo de solo lectura (CQRS ligero) alimentado por eventos,
"para no competir por bloqueos con las conversaciones activas".

**Problema con la copia:** las tres métricas de la Regla 18 — primera respuesta, conversión y
actividad por analista — se derivan enteras de `Mensajes`, `Postulaciones` y `Auditoria`, que ya
existen. Una proyección alimentada por eventos agrega tres cosas que hoy no hacen falta: puede
quedar desincronizada, necesita backfill histórico, y sólo es tan completa como los eventos que
alguien se acordó de publicar. Un panel que miente en silencio es peor que uno lento.

**Decisión:** `Reporting` tiene su propio `ReportingDbContext` —conexión aparte, `NoTracking`, y
`SaveChanges` bloqueado por construcción— sobre las mismas tablas. Se conserva lo que el dossier
buscaba de verdad: el panel no pasa por los servicios de dominio, no puede escribir, y su consulta
no comparte contexto con las conversaciones que se están atendiendo.

**Cuándo revisarlo:** la cadena `RrhhWhatsAppReporting` ya está separada de la principal. Si el
panel empieza a notarse —tiempos de consulta o esperas de bloqueo medibles— el primer paso es
apuntarla a una réplica de lectura, y recién después construir la proyección. A 16.000
interacciones al mes, un período típico son unos pocos miles de filas.

### V16 — El hub vive en la Api y come de la outbox

**Dossier:** la Sección 9.6.3 pide un canal en vivo "hacia el proyecto Frontend".

**Dónde ponerlo:** el hub quedó en la Api, no en el Frontend. El límite del proyecto es que el
Frontend solo habla HTTP con la Api; un hub allá lo obligaría a leer la outbox por su cuenta, que
es exactamente lo que ese límite evita. La bandeja se conecta como un cliente más.

**Quién lo alimenta:** los avisos los produce el Worker, que es otro proceso y no puede empujar a
conexiones que no tiene. En vez de abrirle un endpoint de push a la Api, la Api hospeda su propio
bucle que drena de la outbox los eventos `AnalistaNotificado`. La outbox ya existía justamente
para desacoplar quién produce de quién consume.

**Por qué no se pisan:** el difusor de la Api y el procesador del Worker filtran por tipos
disjuntos, así que leen la misma tabla sin tocar nunca la misma fila —`EventosSistema` no tiene
reserva—. Hay una prueba que falla si algún día los conjuntos se solapan.

**Consecuencia que importa:** `AnalistaNotificado` se publicaba desde el primer día sin consumidor.
El aviso de 48h por formulario sin completar (Regla 9), el multi-cuenta (Regla 6) y el de
escalamiento (Regla 2) se calculaban bien y no llegaban a ninguna persona. La transferencia
(Regla 8) directamente no avisaba: el destino nunca sabía que tenía algo esperando su respuesta.

**Lo que sigue pendiente:** con más de una instancia de Api haría falta un backplane (Redis) para
que un aviso llegue al analista conectado a otra instancia. El grupo del hub ya sale del token, no
de lo que pida el cliente (ver V20).

### V17 — El titular de una cuenta también lleva índice único

**Estado previo:** el modelo garantizaba por índice filtrado que hubiera **un solo respaldo** por
cuenta (Regla 2), pero nada impedía dos titulares. `FabricaContextoRegla` resuelve el titular con
`FirstOrDefault(a => !a.EsBackup)`, así que con dos filas el enrutamiento de la Regla 1 habría
elegido una de forma arbitraria y el otro analista nunca habría visto las conversaciones de su
cuenta.

**Decisión:** se agregó `IX_AnalistaCuenta_TitularUnicoPorCuenta`, filtrado sobre `EsBackup = 0`,
con el mismo razonamiento que ya justificaba el del respaldo. El servicio además reemplaza al
ocupante en vez de fallar, que es lo que un cambio de responsable significa en la práctica.

**Detalle de EF Core que costó una migración descartada:** dos `HasIndex(x => x.CuentaId)` sobre la
misma entidad colisionan, porque EF identifica el índice por sus propiedades y no por su nombre. La
primera migración generada *eliminaba* el índice del respaldo en lugar de agregar el del titular
—habría quitado en silencio la garantía de la Regla 2—. La forma correcta es la sobrecarga que
recibe el nombre: `HasIndex(x => x.CuentaId, "IX_...")`.

### V18 — El alta de cuentas y analistas vive en la Api, no en una migración

**Problema:** hasta ahora no había forma de crear una cuenta, un analista ni su asignación. La
migración siembra etapas, parámetros y plantillas —cosas del producto—, pero cuentas y analistas
dependen de cada empresa, y sembrarlos habría metido datos de un cliente en el código.

**Consecuencia práctica:** el sistema arrancaba y no podía enrutar nada. El menú del bot no tenía
empresas que ofrecer y la Regla 1 no tenía a quién asignarle el hilo. Cargarlos exigía escribir en
SQL Server a mano, que es justo lo que los servicios de dominio existen para evitar.

**Decisión:** `POST /cuentas`, `POST /analistas` y `POST /cuentas/{id}/analistas`, con las
validaciones que las reglas necesitan (nombre de cuenta y email únicos, un titular y un respaldo
por cuenta). `GET /cuentas` muestra la dotación completa, que es la forma de ver si la operación
está lista antes de conectar el WABA.

### V19 — Cada bucle del Worker declara su propia tolerancia de latido

**Dossier:** la Sección 9.6.2 pide un `GET /health` y "una alerta si el Worker deja de procesar".

**Problema:** los tres bucles corren a ritmos muy distintos —la outbox cada 5 segundos, el barrido
cada 5 minutos, la purga de CVs cada 24 horas—. Un único umbral de "sin latir hace X" daría falsas
alarmas sobre la purga o silencio sobre la outbox, que es justamente la que no puede detenerse.

**Decisión:** el latido guarda su propia `ToleranciaSegundos`, que escribe quien late (tres ciclos
de holgura: uno perdido puede ser un ciclo lento, tres seguidos ya no). La Api solo compara
`FechaUtc + tolerancia` contra el reloj, y así no necesita conocer la configuración del Worker.

**Por qué una tabla y no `ConfiguracionReglas`:** el latido es estado operativo, no un parámetro
ajustable. Meterlo ahí lo dejaría a la vista del analista en `GET /configuracion/reglas`, mezclado
con las 2 horas de escalamiento y los 90 días de archivado.

**Lo que el latido no resuelve:** el endpoint se pone en rojo, pero hace falta un monitor externo
que lo consulte y avise. Un endpoint que nadie mira no es una alerta.

### V20 — JWT propio, y el analista sale del token

**Decisión (D6):** autenticación con JWT emitido por el propio sistema, no integración con
Active Directory / SSO. La empresa no tiene hoy un directorio al que integrarse, y `IAutenticacionService`
queda como la costura por donde se reemplaza el día que lo haya, sin tocar controladores ni bandeja.

**Lo que de verdad cambió:** antes el `analistaId` viajaba como parámetro de query o en el cuerpo,
tal como lo plantea la Sección 9.4 del dossier. Con eso la Regla 4 —cada analista ve solo lo
suyo— era decorativa: bastaba cambiar un número para leer y responder conversaciones ajenas. Ahora
sale del claim del token, y se quitó de los DTOs para que no se pueda actuar en nombre de otro.

**Autorización al revés:** una política de respaldo exige token en todo endpoint, y lo público se
marca con `[AllowAnonymous]` uno por uno. Olvidarse de proteger un endpoint nuevo lo rompe en vez
de exponerlo, que es el error que conviene que sea ruidoso.

**Arranque:** tras el despliegue nadie tiene contraseña, así que nadie puede entrar.
`POST /sesion/arranque` fija la primera y se cierra sola en cuanto existe alguna. Se prefirió eso a
sembrar una contraseña por defecto —que sobrevive en producción— o a un segundo secreto de
administración, que sería una credencial más que custodiar.

**Lo que no se hizo:** no hay refresh token. Al vencer la jornada el analista vuelve a entrar; un
refresh agrega una credencial de larga vida que hay que poder revocar, y revocar necesita estado
que hoy no existe. El claim `jti` ya está emitido para cuando haga falta.

### V21 — La transferencia se responde desde la bandeja, sin abrir el chat

**Problema:** la Regla 8 tenía el endpoint para aceptar o rechazar, pero ninguna pantalla lo usaba
ni ninguna consulta decía qué estaba esperando respuesta. Una transferencia no urgente avisaba al
destino y ahí terminaba: el hilo se quedaba con el origen para siempre y, como solo puede haber una
pendiente por conversación, tampoco se podía derivar a otro.

**Decisión:** `GET /transferencias/pendientes` lista lo que espera al analista del token, y la
bandeja lo muestra arriba de la lista en cualquier pestaña. Se decide con lo que trae la tarjeta
—de quién viene, de qué cuenta, sobre quién y el comentario— y no abriendo el chat: el panel del
chat trae la caja de respuesta, y el destino podría escribirle al postulante en un hilo que todavía
no aceptó.

**El origen también se entera:** aceptar y rechazar le llegan por el canal en vivo. Rechazar deja
el hilo con él, y sin el aviso no sabría que le toca derivarlo a otro.

**Queda abierto:** una transferencia que el destino nunca contesta sigue bloqueando cualquier otra
para esa conversación. Hacerla vencer, o dejar que el origen la retire, depende del criterio de
urgencia que RRHH todavía no confirmó (Sección 12 del dossier).

### V22 — La Regla 4 separa ver de actuar, y se aplica en la frontera HTTP

**Problema:** con el token la Api sabía quién pedía, pero solo lo usaba para armar la lista.
Cualquier analista podía abrir cualquier conversación por su id, responderle, transferírsela a sí
mismo como urgente o descartar al postulante en la cuenta de otro. El buscador por DNI y el tablero
cruzaban cuentas, y el detalle del chat mostraba las postulaciones de la persona en todas ellas,
que es justo el detalle que la Regla 6 dice que no se ve.

**Decisión:** un `NivelAcceso` —ninguno, lectura, total— con un solo criterio por objeto:

- **Conversación:** la trabaja quien la atiende, y cualquiera si está sin clasificar (Regla 19).
- **Cuenta** (tablero y tarjetas): la trabajan su titular y su respaldo. El respaldo cuenta como de
  la cuenta porque la Regla 2 le pasa las conversaciones y la 14 las nuevas.
- **Sistemas** ve todo y no actúa. El dossier le da "visibilidad total para soporte y auditoría";
  responder en un hilo ajeno le hablaría al postulante en nombre de alguien que no lo sabe.

Quien no ve recibe 404, no 403: un 403 le confirmaría que el id existe. Quien ve pero no puede
actuar recibe 403 con el motivo.

**Dónde se aplica:** en un filtro del controlador de conversaciones, no dentro de cada acción. Una
acción nueva queda cubierta sin que nadie se acuerde, con el mismo razonamiento que la política de
autorización de respaldo (V20). Una prueba falla si el controlador pierde el filtro. `marcar` además
contrasta el postulante y la cuenta del cuerpo contra la conversación de la ruta.

**Desvío de la Sección 9.4:** mover la tarjeta pasa a `POST /postulaciones/{id}/etapa`. Con la ruta
del dossier el tablero mandaba el id de la postulación donde la Api esperaba el de una
conversación, y ningún control podía validar el objeto correcto.

**Criterios a confirmar con RRHH:**

- El historial por DNI (Sección 6.1) se muestra con las cuentas del analista; de las demás queda el
  aviso genérico de la Regla 6. Si RRHH lo quiere completo, choca con la Regla 6 y hay que elegir.
- Aceptar una transferencia pasa el hilo, no la cuenta: el destino no ve el tablero de la vacante de
  origen si esa cuenta no es suya.
- Anonimizar a un postulante (`DELETE /postulantes/{dni}`) queda para Sistemas, porque borra en
  todas las cuentas.

**Lo que no cubre:** la administración —cuentas, analistas, vacantes, horario, parámetros— y el
panel de métricas quedaban abiertos a cualquier analista con sesión. Es otra pregunta (quién
administra, no quién ve) y la resuelve V23.

### V23 — Rol Jefatura, y quién administra qué

**Dossier:** define dos roles, Analista y Sistemas (Sección 9.2). Pero la Regla 14 dice que la
ausencia la marca "el analista (o su jefe)", y la 18 es un panel "para gerencia". Ninguno de los
dos roles es ese jefe.

**Problema:** cualquier analista con sesión podía dar de alta cuentas y analistas, cambiar el
titular de una cuenta ajena, tocar el horario, cambiar las 2 horas del escalamiento, el tope de
envío o los días de retención de CVs, cerrar vacantes de otras cuentas, registrarle una ausencia a
un compañero —lo que le desvía sus conversaciones nuevas— y ver la actividad de todos.

**Decisión (confirmada con el usuario):** un tercer rol, **Jefatura**. Ve el panel de métricas,
registra ausencias de cualquiera y decide quién es titular y respaldo de cada cuenta. No ve
conversaciones ajenas ni las recibe por transferencia. **Sistemas** conserva todo lo de Jefatura y
además la estructura: alta de cuentas y analistas, horario y parámetros. Las **vacantes** las maneja
quien trabaja la cuenta, porque es quien las abre y les arma el formulario (Reglas 9 y 20), y
Sistemas como soporte. Cada analista registra **su propia ausencia**.

Se prefirió a darle el rol Sistemas a gerencia porque Sistemas ve todas las conversaciones: para
mirar un panel agregado es mucho más de lo que hace falta.

**Cómo se hace cumplir:** dos políticas con nombre, `Estructura` y `Jefatura`, sobre la de respaldo
que exige sesión. Lo que depende del recurso —de qué cuenta es la vacante, de quién es la
ausencia— se decide en el controlador, porque una política por rol no lo sabe. Una prueba recorre
todos los controladores y falla si aparece una escritura sin dueño declarado: política, rol,
público, o una entrada explícita en la lista de las que verifican en el código.

**Sin migración:** el rol se guarda como entero, y `Jefatura` es un valor nuevo del enum.

### V24 — Un candado de SQL Server garantiza un solo Worker activo

**Problema:** `EventosSistema` no tiene reserva por fila. Dos Workers tomarían el mismo evento y
podrían enviar el mismo WhatsApp dos veces, que es el patrón que causó el bloqueo original. Estaba
documentado como "correr una sola instancia", y en desarrollo quedaron dos corriendo sin que nadie
lo notara; el health tampoco, porque los dos latían sobre la misma fila.

**Decisión:** el Worker toma `sp_getapplock` en modo sesión antes de arrancar sus bucles y lo
mantiene mientras vive. Una segunda instancia queda en espera y toma el relevo si la primera muere.

**Por qué en la base y no un mutex o un archivo:** es lo único que ven todas las instancias
posibles, en la misma máquina o en otra. Y SQL Server lo suelta al terminar la sesión, así que un
proceso caído no lo deja tomado. La conexión va sin pool por lo mismo: con pool, cerrarla la
devuelve con la sesión viva y el candado tomado por una conexión que nadie usa.

**Por qué esperar y no salir:** una segunda instancia accidental queda inofensiva, y una puesta a
propósito sirve de relevo. Salir obligaría a elegir entre un bucle de reinicios del servicio o un
Worker caído hasta que alguien lo note.

**Dónde:** una guardia registrada antes que los bucles. El host arranca los servicios de a uno y en
orden, así que ninguno empieza hasta que ella tiene el candado; el arranque secuencial se fija
explícitamente para no depender del valor por defecto. Si la activa pierde el candado —se cayó la
conexión— detiene el proceso con código 1, para que el administrador de servicios la reinicie.

**Lo que no resuelve:** entre que la conexión se cae y la activa lo nota hay una ventana —la
verificación, más el lote en curso— en la que otra podría tomar el candado. Es mucho menor que el
riesgo anterior, dos Workers procesando siempre, pero no es cero. Correr varias instancias a
propósito sigue exigiendo la reserva por fila.

**Interfaz local:** `ICandadoInstancia` vive en el Worker y existe para probar la guardia sin SQL
Server; no es una frontera entre módulos. El candado real se prueba contra SQL Server con la
variable `RRHH_PRUEBAS_SQL`, y esas pruebas se omiten si no está.

### V25 — El arranque crea al primer analista de Sistemas, y solo a ese

**Problema:** una base nueva no se podía poner en marcha. La migración no siembra analistas —son
datos de cada empresa (V18)—, dar de alta uno exige ser Sistemas (V23), y `POST /sesion/arranque`
solo fijaba la contraseña de un analista que ya existiera: el primero había que insertarlo a mano
en SQL Server. Había además un bloqueo más silencioso: el arranque aceptaba cualquier rol. Si la
primera contraseña iba a un analista común, la puerta se cerraba y nadie podía restablecer las de
los demás, porque eso es solo de Sistemas.

**Decisión:** el arranque es solo para Sistemas. Si no existe un analista con el correo pedido, lo
da de alta con ese rol, pero solo si el correo coincide con `Arranque:EmailSistemas`, que se define
por variable de entorno en el servidor. El correo se compara sin distinguir mayúsculas, como lo
guarda el alta.

**Por qué acotarlo al correo configurado:** el arranque es anónimo, y la Api es alcanzable desde
internet porque recibe el webhook de Meta. Crear sin esa condición le daría visibilidad total a
cualquiera que llegara primero a un despliegue recién publicado. Con ella, la exposición queda
igual que en V20: hay que conocer un correo, y la puerta se cierra con la primera contraseña. El
correo no es un secreto que haya que custodiar —V20 descartó sumar uno—: acota, no autentica.

**Alternativas descartadas:** sembrarlo en una migración mete datos de una empresa en el código y
deja un usuario conocido en todas las instalaciones. Un script que lo inserte en SQL se saltea las
validaciones del alta de analistas —el correo normalizado, el rol—, que es lo que V18 quiso evitar.

### V26 — Despliegue: dos sitios de IIS y el Worker como servicio

**Dossier:** despliegue on-premise sobre IIS (D5), sin decir cómo.

**Decisión:** la Api y la bandeja como **dos sitios separados**, y el Worker como **servicio de
Windows**. Separarlos es lo que permite que el webhook de Meta entre por una URL pública con
certificado y la bandeja quede solo en la red interna; además, reciclar un sitio no toca al otro.

**Lo que el pool necesita, y no es el valor por defecto:** sin apagado por inactividad y sin
reciclado por tiempo. Los dos matan el proceso, y con él los circuitos de Blazor y el canal en vivo
(Sección 9.6.3): el analista vería su bandeja congelarse en medio de una conversación. Por eso
`instalar-iis.ps1` los pone en cero en vez de dejar los 20 minutos y las 29 horas de siempre.

**El servicio con recuperación ante fallas** es la otra mitad de V24: el Worker termina con código 1
cuando pierde el candado, y sin recuperación quedaría detenido hasta que alguien lo notara — con las
reglas por tiempo sin correr. Se activa además `failureflag`, porque por defecto Windows solo
reacciona a caídas del proceso y no a una salida con código de error.

**ContentRoot explícito en el Worker:** un servicio arranca en `System32`. Con el directorio actual
no encontraría su `appsettings.json`, y el fallo aparecería como "no hay cadena de conexión", que no
lleva a ninguna parte.

**Los secretos van por variables de entorno de máquina**, no en los `appsettings.json` publicados:
los leen los tres procesos por igual y no viajan en el paquete que se copia al servidor. Es lo mismo
que ya pedía la Sección 9.6.1, llevado a la forma concreta del servidor.

**Lo que los scripts no hacen a propósito:** el binding HTTPS con el certificado de la empresa. Es
la URL que ve Meta y toca material sensible; armarla a ciegas desde un script esconde justo lo que
conviene revisar a mano.

### V27 — El token viaja donde diga la vacante, y el envío del formulario es idempotente

**Problema 1:** el enlace se armaba agregando `?t=<token>`. Google Forms solo prellena parámetros
con la forma `entry.<id>=`, distinta en cada formulario, así que ese token nunca llegaba a la
respuesta: el webhook recibiría envíos que no puede atribuir a ninguna postulación. El circuito del
JobForms no podía funcionar con Google Forms tal como estaba.

**Decisión:** la URL de la vacante puede traer el marcador `{token}`, y el sistema lo reemplaza. Sin
marcador se sigue agregando `?t=`, que es lo que va a leer el formulario propio cuando se migre a
Razor Pages (D3). El nombre del parámetro no puede vivir en la configuración porque cambia con cada
formulario: es un dato de la vacante, y ahí queda.

**Problema 2:** el webhook no reconocía un envío repetido. El Apps Script reintenta cuando se pierde
la respuesta —y perderla es normal en una red—, y cada reintento guardaba otra respuesta y volvía a
publicar el evento: el postulante recibía la confirmación dos veces. Es el mismo patrón de mensajes
duplicados que le costó la línea a la empresa.

**Decisión:** si la invitación ya está completada y su respuesta existe, el envío no se procesa de
nuevo y la Api contesta `yaRecibido`. El script lo trata como éxito. Se prefirió resolverlo en la
Api antes que pedirle al script que no reintente: un reintento que falta deja al postulante sin
postulación, y eso es peor que uno de más.

**Lo que el script no hace:** decidir. Valida lo mínimo para no mandar basura —que haya token y
DNI— y el resto lo resuelve la Api: vacante abierta (Regla 20), consentimiento (Regla 17) y a quién
pertenece el envío. El día que el formulario se migre a Razor Pages, el script se tira y no cambia
nada más.

## Riesgos abiertos

| Riesgo | Detalle | Mitigación |
|---|---|---|
| **Login de Google para subir el CV** | Google Forms exige que el postulante inicie sesión con cuenta Google para adjuntar archivos. En reclutamiento masivo eso tumba parte del embudo. | Medir la caída en el piloto. Si es alta, adelantar la migración a Razor Pages (D3). |
| **Regla 17 sin trazabilidad por versión** | Con Google Forms se sella la versión vigente al momento del envío, no la que el postulante realmente vio. | `VersionAvisoPrivacidad` ya existe en el modelo; se llena bien al migrar a Razor Pages. |
| **Regla 20 sólo parcialmente automática** | El analista todavía debe desactivar el formulario de Google a mano al cerrar la vacante: nada nos deja apagarlo desde acá. | El sistema ya no depende de eso. El bot no manda el enlace de una vacante cerrada, y el envío del formulario se rechaza aunque la vacante se cierre mientras el postulante lo llena. Lo único que queda expuesto es el formulario de Google en sí. |
| **Webhook público** | 360dialog necesita una URL HTTPS con certificado válido. On-premise implica DNS, certificado y regla de firewall, con plazo propio. | Iniciarlo en paralelo al desarrollo, como la aprobación del WABA. |
| **SQL Server Express** | 10 GB por base y sin SQL Agent. | Suficiente para el volumen actual; los CVs van fuera de la BD y el Worker reemplaza al Agent. Confirmar la instancia de producción. |
| **Aprobación del WABA** | Es el cuello de botella real del proyecto, no el desarrollo. | Iniciar el trámite desde el día 1 (Sección 10 del dossier). |
| **Worker de instancia única** | `EventosSistema` no tiene reserva por fila. Dos Workers tomarían el mismo evento y podrían enviar el mismo mensaje dos veces, que es el patrón que causó el bloqueo original. | Candado de SQL Server (V24): una segunda instancia queda en espera. Queda una ventana corta si la activa pierde la conexión. Si el volumen desborda a una instancia, agregar reserva por fila antes de escalar, no después. |
| **La marca de "reingreso" no existe en el modelo** | Las Reglas 9 y 16 dicen "salvo marca de contratado / descartado / reingreso", pero `EstadoPostulacion` solo tiene `EnProceso`, `Contratado`, `Descartado` y `Archivada`. Hoy las reglas deciden con lo que existe: la 16 no archiva si hay algo en proceso o contratado, y la 9 no repregunta si el analista ya decidió. | Confirmar con RRHH qué significa reingreso en la práctica antes de agregar el estado; el mini-cuestionario de estado del postulante ya estaba pendiente de definición en la Sección 12 del dossier. |
| **El CV vive en Google Drive** | Con Google Forms el adjunto queda en Drive y sólo guardamos su enlace. La purga de la Regla 17 limpia la referencia pero no puede borrar el archivo en el origen. | El Worker lo registra en el log cada vez que ocurre, para que quede el rastro del paso manual. Se resuelve solo al migrar el formulario a Razor Pages (D3), donde el CV entra por `IAlmacenamientoCv`. |

## Convenciones

- **Identificadores en español sin tildes** (`ConversacionId`, `FechaEnvio`). Nombres de tabla en
  español con la forma del dossier.
- **Fechas en UTC** en base de datos; se muestran en `America/Lima` (UTC-5, sin horario de verano).
- **Las reglas deciden, no ejecutan.** Cada `IReglaNegocio` devuelve `AccionRegla`; la capa de
  aplicación las ejecuta. Es lo que permite probarlas sin base de datos ni proveedor.
- **Parámetros en `ConfiguracionReglas`**, nunca literales en el código: 2 horas, 3 días, 90 días,
  topes de envío.
