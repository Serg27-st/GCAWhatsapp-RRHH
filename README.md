# RRHH WhatsApp — Automatización de reclutamiento

Sistema interno que reemplaza el uso de WhatsApp Business desde múltiples PCs por la WhatsApp
Business API oficial, con bandeja multi-agente para los 13 analistas de RRHH y ~20 cuentas.

Requisitos y reglas de negocio: [`docs/Dossier_Maestro_WhatsApp_RRHH_v2.docx`](docs).
Decisiones de diseño y desviaciones respecto del dossier: [`docs/decisiones.md`](docs/decisiones.md).

## Stack

- .NET 10 / ASP.NET Core
- Entity Framework Core 10 + SQL Server (migraciones versionadas)
- Blazor Server para la bandeja del analista
- 360dialog como proveedor de WhatsApp Business API
- Despliegue on-premise (IIS + SQL Server de la empresa)

## Estructura

```
src/
  RRHH.WhatsApp.Domain          entidades, enums, interfaces y las reglas de negocio como Strategy
  RRHH.WhatsApp.Application     motor de reglas y casos de uso
  RRHH.WhatsApp.Contracts       DTOs compartidos entre Api y Frontend
  RRHH.WhatsApp.Infrastructure  EF Core, adaptador de 360dialog, almacenamiento de CVs
  RRHH.WhatsApp.Api             webhook + endpoints REST
  RRHH.WhatsApp.Worker          lógica disparada por tiempo (2h, 24h, 48h, 90 días)
  RRHH.WhatsApp.Reporting       modelo de solo lectura para las métricas de gerencia
  RRHH.WhatsApp.Frontend        bandeja del analista (Blazor Server)
tests/
  RRHH.WhatsApp.Tests           una prueba por regla de negocio
```

Dependencias: `Domain` no referencia a nadie. `Frontend` solo referencia `Contracts` y habla con
la Api por HTTP — nunca toca SQL Server.

## Puesta en marcha

Compilar y correr las pruebas:

```bash
dotnet test
```

Crear o actualizar la base de datos local:

```bash
dotnet ef database update --project src/RRHH.WhatsApp.Infrastructure
```

Por defecto apunta a `.\SQLEXPRESS` / `RRHH_WhatsApp` con autenticación integrada. Para otro
servidor, definir la variable de entorno `RRHH_CONNECTIONSTRING`.

Agregar una migración tras cambiar el modelo:

```bash
dotnet ef migrations add NombreDelCambio --project src/RRHH.WhatsApp.Infrastructure --output-dir Persistencia/Migraciones
```


### Correr el sistema

Son tres procesos, cada uno en su terminal. La Api primero: el Worker y la bandeja dependen de ella.

```bash
dotnet run --project src/RRHH.WhatsApp.Api --urls http://localhost:5087
```

```bash
dotnet run --project src/RRHH.WhatsApp.Worker
```

```bash
dotnet run --project src/RRHH.WhatsApp.Frontend --urls http://localhost:5090
```

La bandeja queda en <http://localhost:5090>. Si cambiás el puerto de la Api, ajustá `Api:BaseUrl`
en el `appsettings.json` del Frontend.

Qué hace cada uno: la **Api** recibe el webhook de WhatsApp y sirve la bandeja; el **Worker**
consume la outbox y corre las reglas por tiempo —sin él, un mensaje entrante se guarda pero nadie
lo responde—; el **Frontend** es la pantalla del analista.

### Dejar la operación en condiciones de funcionar

Recién instalado, el sistema arranca pero no puede enrutar nada: la migración siembra las etapas
del kanban, los parámetros y las plantillas, pero **no las cuentas ni los analistas**, que dependen
de cada empresa. Sin al menos una cuenta con titular y una vacante abierta, el bot no tiene qué
ofrecer y la Regla 1 no tiene a quién asignarle la conversación.

**1. El primer analista de Sistemas.** Una base nueva no tiene ninguno, y dar de alta analistas es
de Sistemas (V23): alguien tiene que ser el primero. Antes de levantar la Api, definí qué correo lo
va a ser:

```bash
setx Arranque__EmailSistemas "sistemas@empresa.pe"
```

Con la Api levantada, `POST /sesion/arranque` lo da de alta con rol Sistemas y le fija la
contraseña (mínimo 10 caracteres). Hacelo apenas publicada: la puerta se cierra sola en cuanto
existe la primera contraseña, y hasta entonces la puede usar cualquiera que conozca ese correo.

```bash
curl -X POST http://localhost:5087/sesion/arranque -H "Content-Type: application/json" -d '{"nombre":"Sistemas","email":"sistemas@empresa.pe","contrasena":"..."}'
```

**2. El resto, desde la bandeja.** Entrá con ese usuario en <http://localhost:5090>:

- **Equipo:** las cuentas, los analistas con su contraseña inicial, y el titular y el respaldo de
  cada cuenta (Reglas 1 y 2).
- **Vacantes:** al menos una vacante abierta por cuenta, con el enlace de su formulario (Regla 9).
- **Configuración:** el horario de atención (Regla 3).

`GET /cuentas` devuelve cada cuenta con su titular, su respaldo y sus vacantes abiertas: es la
forma rápida de ver si falta algo. Una cuenta sin titular, o sin vacantes abiertas, no aparece en
el menú del bot.

Para probar el circuito sin WhatsApp, `scripts/probar-webhook-local.ps1` arma un payload con la
forma exacta de la Cloud API y lo firma como lo firma Meta, así que ejercita el camino real
—firma, idempotencia, opt-in, outbox y reglas— sin túnel ni número de prueba:

```powershell
.\scripts\probar-webhook-local.ps1 -Telefono '+51955111222' -Texto 'Hola, vi el aviso'
```

Repetir el mismo `-Wamid` dos veces es la forma de comprobar la idempotencia: la segunda entrega
debe responder `duplicados=1`. Sin credenciales de proveedor el envío no sale a la red y queda en
el log del Worker como `[SIMULADO]`.

## Estado

| Componente | Estado |
|---|---|
| Modelo de datos + migración inicial | listo — 20 tablas, semillas de etapas kanban, parámetros y plantillas |
| Contratos de dominio (Sección 9.3) | listo |
| Motor de reglas | listo — conectado: el webhook encola, el Worker consume y ejecuta |
| Reglas implementadas | 1, 2, 3, 7, 9, 12, 13, 14, 15, 16, 17, 18, 19, 20 |
| Reglas cubiertas sin clase propia | 4 (visibilidad, en la consulta de la bandeja), 5 (efecto de la 1 automática), 6 (aviso multi-cuenta en la R1 y en el detalle), 8 (transferencias, en el servicio), 10 (el bot se identifica en el menú), 11 (es el aviso de la 6) |
| Reglas pendientes | ninguna — las 20 están cubiertas |
| Adaptador 360dialog | listo — envío de texto, plantillas y botones, con límite de velocidad |
| Webhook entrante | listo — firma, idempotencia, opt-in, outbox y acuses de entrega |
| Servicios de dominio | listo — conversaciones, mensajes, postulantes, postulaciones, JobForms |
| Circuito JobForms | listo — envío del enlace, webhook de Google, respuesta, CVs y confirmación |
| Consumidor de la outbox | listo — lotes con reintentos; los eventos sin dueño quedan en cola |
| Barrido por tiempo del Worker | listo — Reglas 2, 9 y 16 |
| Purga de CVs (Regla 17) | listo — retención configurable y `DELETE /postulantes/{dni}` |
| Reintento de envíos salientes | listo — por mensaje, solo lo transitorio, revalidando la Regla 15 |
| Endpoints (Sección 9.4) | listo — bandeja, respuesta, transferencias, kanban, cuentas, analistas, HC, ausencias, plantillas, horario |
| Contratos compartidos (`Contracts`) | listo — DTOs de bandeja y métricas, que es lo único que verá el Frontend |
| Bandeja del analista (Blazor) | listo — lista por cuenta, chat, respuesta, acciones rápidas, kanban, métricas y administración por rol |
| Reporting (Regla 18) | listo — modelo de solo lectura propio y `GET /reportes/metricas` |
| Autenticación de analistas (Sección 9.6.1) | listo — JWT propio, el analista sale del token |
| Tiempo real por SignalR (Sección 9.6.3) | listo — hub por analista, alimentado desde la outbox |

El sistema elige proveedor solo, en este orden:

| Si está configurado | Usa | Cuándo conviene |
|---|---|---|
| `MetaCloud:AccessToken` + `PhoneNumberId` | Cloud API de Meta | **Número de prueba gratis.** Para probar de verdad antes de contratar nada |
| `Dialog360:ApiKey` | 360dialog | Producción, según la decisión D2 |
| ninguno | Proveedor simulado | Registra los envíos en el log en vez de salir a la red |

Los tres comparten los cuerpos de mensaje y el intérprete del webhook, porque 360dialog es un paso
a través de la Cloud API: lo que cambia entre ellos es la URL, la cabecera de autenticación y cómo
viene firmado el webhook.

### Nota de operación

`Mensajes` tiene un índice único filtrado sobre `ProviderMessageId`. SQL Server exige
`QUOTED_IDENTIFIER ON` para escribir en una tabla así: EF Core lo activa solo, pero un script
manual desde `sqlcmd` falla si no lo pone primero.

```bash
sqlcmd -S ".\SQLEXPRESS" -d RRHH_WhatsApp -E -C -Q "SET QUOTED_IDENTIFIER ON; ..."
```




### La bandeja

Blazor Server, `RRHH.WhatsApp.Frontend`. Habla solo HTTP con la Api: no referencia el dominio ni
toca SQL Server, y todo lo que muestra viene de los DTOs de `Contracts`.

Para levantarla en desarrollo hacen falta los dos procesos:

```bash
dotnet run --project src/RRHH.WhatsApp.Api --urls http://localhost:5087
```

```bash
dotnet run --project src/RRHH.WhatsApp.Frontend --urls http://localhost:5090
```

La URL de la Api se configura en `Api:BaseUrl`.

Lo que hay, contra la Sección 7 del dossier:

| Pantalla | Cubre | Quién la ve |
|---|---|---|
| `/bandeja` | lista por cuenta, buscador por DNI, chat, respuesta, acciones rápidas y transferencias recibidas | todos |
| `/vacantes` | vacantes de cada cuenta: crear, cerrar, campos del formulario y entrada al tablero | titular y respaldo de la cuenta, y Sistemas |
| `/tablero/{hcId}` | kanban por vacante, con columnas arrastrables (Regla 13) | titular y respaldo; Sistemas, sin mover tarjetas |
| `/metricas` | panel de gerencia (Regla 18) | Jefatura y Sistemas |
| `/equipo` | titular y respaldo de cada cuenta y ausencias de cualquiera; alta de cuentas y analistas y restablecer contraseñas, solo Sistemas | Jefatura y Sistemas |
| `/configuracion` | horario de atención, parámetros de reglas y estado de las plantillas | Sistemas |
| `/mi-cuenta` | cambiar la propia contraseña y las propias ausencias | todos |

Las plantillas se ven pero no se activan desde la pantalla: se marcan activas en la base recién
cuando Meta las aprueba (V8). Los parámetros se validan contra el tipo del valor actual y un número
no admite cero, porque 0 días de retención purgaría todos los CVs en la próxima vuelta del Worker.

Cuatro cosas que la pantalla hace explícitas porque el analista necesita verlas:

- **Lo que le transfirieron** (Regla 8) va arriba de la lista, en cualquier pestaña, con aceptar y
  rechazar. Se decide con la tarjeta —quién la manda, de qué cuenta, sobre quién y el comentario—
  sin abrir el chat, que todavía no es suyo. Aceptar abre el hilo; la respuesta, sea cual sea, le
  llega al origen por el canal en vivo, porque si la rechazaron el hilo sigue siendo suyo.

- **La ventana de 24 horas.** Una conversación fuera de ventana se marca en la lista, y la caja de
  respuesta bloquea el texto libre y obliga a elegir plantilla. Quien decide es la Regla 15 en la
  Api; la pantalla solo refleja lo que responde.
- **Las plantillas sin aprobar no aparecen** en el selector, y se avisa cuántas están esperando a
  Meta. Sin eso, el analista intentaría usar una y no entendería el rechazo.
- **El aviso multi-cuenta** (Regla 6) sale en la lista y en la cabecera del chat, genérico y sin
  dejar ver las conversaciones de la otra cuenta.

**El canal en vivo.** Escalamientos, transferencias y avisos de formulario sin completar llegan a
la bandeja sin que nadie refresque (Sección 9.6.3). El hub vive en la Api (`/hub/bandeja`), con un
grupo por analista —sin eso, un aviso destinado a uno llegaría a todos y la Regla 4 quedaría rota
también en el canal—. La bandeja se conecta como cliente HTTP, así que sigue sin conocer la base.

El circuito completo es: una regla decide notificar → el aviso queda en la outbox → un bucle en la
Api lo drena y lo empuja al hub → la bandeja recarga su lista. **El intervalo sigue activo como
respaldo**: si el hub está caído o un aviso se pierde, la bandeja se pone al día igual.

El hub va autenticado con el mismo token: el grupo se toma del claim, no de lo que pida el cliente.
SignalR no puede mandar cabecera `Authorization` al negociar por WebSocket, así que el token viaja
por query string — y solo se acepta ahí, en la ruta del hub.

### El panel de gerencia

`GET /reportes/metricas?desde=&hasta=` devuelve las tres métricas de la Regla 18 —tiempo de
primera respuesta, tasa de conversión y actividad por analista— más el desglose por cuenta. Sin
período, cubre los últimos 30 días.

Dos criterios que conviene conocer antes de leer el número:

- **La primera respuesta cuenta sólo respuestas de una persona.** El menú del bot sale en
  segundos; incluirlo daría un promedio que no dice nada del servicio.
- **El promedio viene con su mediana.** Un solo mensaje respondido el lunes por la mañana después
  de llegar el sábado distorsiona el promedio de toda la semana.

El panel corre sobre `RRHH.WhatsApp.Reporting`, que tiene su propio contexto de solo lectura y no
pasa por los servicios de dominio. Usa la cadena `RrhhWhatsAppReporting` si está definida, y si no
la principal — separarla es lo que permite apuntarla a una réplica el día que el volumen lo pida.

### Autenticación

JWT propio (Sección 9.6.1). Se eligió sobre AD/SSO porque la empresa no tiene hoy un directorio al
que integrarse; `IAutenticacionService` queda como la interfaz que se reemplaza el día que lo haya.

**Todo endpoint exige token salvo los que se marcan como públicos** — el webhook de WhatsApp, el
circuito del JobForms, el login y la salud. Es al revés de lo habitual a propósito: olvidarse de
proteger un endpoint nuevo debería romperlo, no exponerlo.

Lo que cambia de fondo es de dónde sale el analista. Antes el `analistaId` viajaba como parámetro,
así que la Regla 4 era decorativa: cualquiera podía pedir la bandeja de cualquiera cambiando un
número. **Ahora sale del token**, y lo mismo vale para el hub en vivo: el grupo se toma del claim,
no de lo que pida el cliente.

**Qué ve cada uno (Regla 4).** Tener sesión no alcanza: toda ruta `/conversaciones/{id}` pasa por un
filtro que pregunta qué puede hacer ese analista con ese hilo.

| Quién | Sobre qué | Puede |
|---|---|---|
| El analista que la atiende | su conversación | ver y actuar |
| Cualquiera | lo que está sin clasificar (Regla 19) | ver y actuar |
| Sistemas | todo | ver, no actuar: no responde en nombre de otro |
| El resto | — | nada: la Api responde 404, como si no existiera |

El mismo criterio, por cuenta, vale para el tablero y las tarjetas (titular y respaldo), para el
buscador por DNI —que no cruza cuentas— y para el historial del postulante. Anonimizar a una
persona (`DELETE /postulantes/{dni}`) queda para Sistemas, porque borra en todas las cuentas. El
detalle, con sus criterios pendientes de confirmar, está en la decisión V22.

**Quién administra (V23).** Hay tres roles: Analista, Jefatura y Sistemas.

| Qué | Quién |
|---|---|
| Alta de cuentas y analistas, horario, parámetros de reglas | Sistemas |
| Titular y respaldo de cada cuenta, ausencias de otros, panel de métricas | Jefatura y Sistemas |
| Crear, cerrar y configurar vacantes | titular o respaldo de esa cuenta, y Sistemas |
| La propia ausencia | cada analista |
| Catálogos: analistas, cuentas, vacantes abiertas, plantillas, etapas, horario | todos, solo lectura |

Jefatura no ve conversaciones ajenas ni recibe transferencias: mira el panel y decide la
cobertura. Una prueba recorre todos los controladores y falla si aparece una escritura sin dueño
declarado.

| Ruta | Para qué |
|---|---|
| `POST /sesion/login` | Devuelve el token y su vencimiento |
| `GET /sesion/yo` | Quién soy según el token |
| `PUT /sesion/contrasena` | Cambiar la propia; exige la actual |
| `PUT /sesion/analistas/{id}/contrasena` | Restablecer la de otro — solo rol Sistemas |
| `POST /sesion/arranque` | La primera contraseña del sistema |

**La primera vez.** Tras el despliegue ningún analista tiene contraseña, así que nadie puede
entrar. `POST /sesion/arranque` fija la primera y **se cierra sola en cuanto existe** — de ahí en
adelante devuelve 409 y las contraseñas se manejan por restablecimiento. Es **solo para Sistemas**:
es el único rol que después puede restablecer las de los demás, así que una primera contraseña de
otro rol dejaría a todos afuera. En una base nueva, si el correo coincide con
`Arranque__EmailSistemas`, además da de alta al analista (V25). Los pasos están en *Dejar la
operación en condiciones de funcionar*.

**Detalles que importan.** Las contraseñas se guardan con PBKDF2-SHA256, sal por usuario y 210.000
iteraciones: la lentitud es la defensa. El login responde lo mismo ante usuario inexistente,
inactivo o contraseña incorrecta —y tarda lo mismo— para no delatar qué correos son reales. Va
detrás del mismo límite de velocidad que los endpoints públicos.

**Sin `Jwt__Clave` la Api no arranca.** Firmar con un relleno daría tokens que cualquiera puede
falsificar y el sistema andaría igual: es la clase de fallo que nadie nota hasta que alguien lo
aprovecha. Mínimo 32 caracteres, por variable de entorno, nunca versionada.

El token dura una jornada (`Jwt:VigenciaHoras`, 9 por defecto). No hay refresh: al vencer, el
analista vuelve a entrar. La bandeja no lo persiste, así que cerrar el navegador cierra la sesión.


### Probar con el número de prueba gratis de Meta

Meta entrega un número de prueba sin costo al crear una app en
[developers.facebook.com](https://developers.facebook.com). Alcanza para recorrer el flujo entero
—recibir, enrutar, responder, plantillas— desde tu celular, sin contratar 360dialog ni esperar la
aprobación del WABA.

**Sus límites:** solo escribe a hasta 5 números que hayas registrado como destinatarios de prueba,
el número emisor es de Meta y no el de la empresa, y el token de la consola vence a las 24 horas.
Para producción sigue haciendo falta el WABA propio.

#### 1. En Meta (esto lo hacés vos)

1. Creá una app de tipo **Empresa** y agregale el producto **WhatsApp**.
2. En **WhatsApp → Configuración de la API** vas a ver el número de prueba ya creado. Anotá el
   **Identificador del número de teléfono** (no el número: el id).
3. En esa misma pantalla, **agregá tu celular** como destinatario de prueba y confirmá el código.
   Sin esto Meta no te deja enviar nada.
4. Copiá el **token de acceso temporal** de la consola.
5. En **Configuración → Básica**, copiá la **clave secreta de la app**.

#### 2. Una URL pública para el webhook

Meta necesita alcanzar tu máquina por HTTPS, así que hace falta un túnel. Cualquiera sirve:

```bash
cloudflared tunnel --url http://localhost:5087
```

Te devuelve una URL `https://algo.trycloudflare.com`. Dejalo corriendo: si lo cerrás, la URL
cambia y hay que volver a darla de alta en Meta.

#### 3. Configurar el sistema

Las credenciales van por variable de entorno, nunca al `appsettings.json`:

```bash
setx MetaCloud__PhoneNumberId "el-id-del-numero"
setx MetaCloud__AccessToken "el-token-de-la-consola"
setx MetaCloud__AppSecret "la-clave-secreta-de-la-app"
setx MetaCloud__TokenVerificacion "una-cadena-que-inventes"
```

`TokenVerificacion` es tuya: la inventás acá y la repetís en Meta en el paso siguiente. Sirve para
que Meta compruebe que el endpoint es de quien dice.

Abrí una consola nueva —`setx` no afecta a las ya abiertas— y levantá los tres procesos.

#### 4. Dar de alta el webhook en Meta

En **WhatsApp → Configuración → Webhooks**, editá y poné:

- **URL de devolución de llamada:** `https://tu-tunel.trycloudflare.com/webhook/whatsapp`
- **Token de verificación:** la misma cadena de `MetaCloud__TokenVerificacion`

Meta hace un GET de verificación al guardar. Si responde bien, queda verificado; si da 403, el
token no coincide.

Después **suscribite al campo `messages`**. Sin eso Meta verifica la URL pero no te manda nada.

#### 5. Probar

Escribile desde tu celular al número de prueba. Deberías ver, en orden:

1. El log de la Api: `Webhook procesado: 1 nuevos`
2. El log del Worker: el motor evaluando las reglas
3. En tu WhatsApp: el menú de empresas del bot (Regla 19)
4. En la bandeja (`http://localhost:5090`): la conversación, con el hilo

Si no llega nada, mirá en ese orden: el túnel sigue vivo, Meta muestra el webhook como verificado
y suscrito a `messages`, y `GET /health` está en verde —si el Worker está caído, el mensaje entra
pero nadie evalúa las reglas—.

**Las plantillas son aparte.** Las 6 sembradas están inactivas hasta que Meta las apruebe, así que
el bot responde con texto libre y menús, que es lo permitido dentro de la ventana de 24 horas. Para
probar una plantilla, creala en **WhatsApp → Plantillas de mensajes**, esperá la aprobación y
recién ahí marcá `Activa = true` en la tabla `Plantillas` con el mismo `NombreMeta`.

**El token vence a las 24 horas.** Cuando empiece a fallar el envío con un error de autenticación,
o volvés a copiar el temporal, o generás uno permanente con un usuario del sistema en Business
Manager, que es lo que corresponde si vas a dejarlo andando.
### El circuito del JobForms

El postulante elige la empresa por botones, el bot le manda el enlace del formulario de esa
vacante, y al completarlo el sistema lo devuelve al chat con la confirmación y su postulación ya
creada.

El enlace lleva un token aleatorio, no el `HCId`: con el id secuencial se podrían enumerar
vacantes de otras cuentas cambiando el número en la URL. Cada `HC` necesita su `UrlJobForms`
cargada; si está abierta pero sin formulario, el envío se omite y queda un evento
`VacanteSinFormulario` en la outbox.

Endpoints públicos, los únicos alcanzables desde internet sin autenticación:

| Ruta | Para qué |
|---|---|
| `GET /jobforms/{token}` | El formulario pregunta si el enlace sigue vigente (Regla 20) |
| `POST /jobforms/webhook-google` | Lo llama el Apps Script al enviarse el formulario |
| `POST /jobforms/{token}/enviar` | Camino propio, para cuando se migre a Razor Pages |

`GET /jobforms/{token}` y `POST /jobforms/{token}/enviar` van detrás de un límite de
`JobForms:LimitePorMinuto` (30 por defecto) solicitudes por minuto y por IP.
`POST /jobforms/webhook-google` tiene el suyo propio (COR-15): con el secreto compartido correcto
en la cabecera `X-JobForms-Secreto`, todas las llamadas comparten el cupo de
`JobForms:LimitePorMinutoWebhook` (600 por defecto) sin importar de qué IP salgan —las IPs de
salida de Apps Script son compartidas entre scripts de distintos dueños, así que un límite por IP
dejaría fuera al script legítimo junto con quien abusa—; sin el secreto correcto, o sin
`JobForms__SecretoWebhook` configurada, cae al mismo límite por IP que los otros dos endpoints, con
un cupo aparte para que agotarlo no afecte a quienes sí llaman sin autenticarse. El webhook de
Google exige además el secreto compartido para no rechazar con 401; sin `JobForms__SecretoWebhook`
configurada rechaza todo, que es el comportamiento buscado: preferimos no recibir nada antes que
aceptar un envío que no podemos atribuir.

**Lo que se valida del cuerpo (COR-15/M8).** Los dos caminos —`webhook-google` y `{token}/enviar`—
normalizan el DNI con `DocumentoIdentidad.TryNormalizar` (Domain, sin dependencias): 8 dígitos
(DNI) o 9 a 12 alfanuméricos (carné de extranjería), sin espacios, guiones ni puntos, en
mayúsculas. Nulo o con otro formato responde `422`, nunca el `500` que daba antes un DNI nulo. El
DNI nunca va a un log ni a un mensaje de error con su valor (CLAUDE.md): si algo falla, el motivo
es genérico.

`CvUrl` (solo en `webhook-google`; el camino propio recibe el CV como archivo) tiene que ser una
URL absoluta `https` cuyo host coincida **exacto** con uno de `JobForms:DominiosCvPermitidos`
(`drive.google.com`, `docs.google.com` por defecto) — no `EndsWith` ni `Contains`, que dejarían
pasar `drive.google.com.evil.com` o `https://evil.com/drive.google.com`. La propiedad nace `null` a
propósito: si trajera el default ya cargado, el binder de configuración de .NET **sumaría** los
índices de `appsettings.json` en vez de reemplazarlos. Se lee siempre a través de
`DominiosCvPermitidosEfectivos`.

**CV huérfano (`{token}/enviar`).** Antes de tocar el disco se revalida que la invitación exista y,
si no está completada todavía, que la vacante siga abierta (Regla 20) — puede haberse cerrado
mientras el postulante llenaba el formulario. Si la invitación ya está completada (V27, reintento
idempotente), no se guarda ningún archivo: `recepcion.ProcesarAsync` responde `yaRecibido` sin
tocar el disco. Si el CV se guardó y `recepcion.ProcesarAsync` termina rechazando el envío (o
resulta que ya estaba recibido), el archivo se borra con `IJobFormsService.EliminarCvAsync` en
mejor esfuerzo: si el borrado falla, queda en el log de error, pero no tapa la respuesta original.

**Campos opcionales por vacante.** Además de los campos fijos (DNI, CV), cada `HC` puede tener sus
propias preguntas, configuradas por el analista:

| Ruta | Para qué |
|---|---|
| `GET /hc/{id}/campos` | Los campos configurados, para editarlos |
| `PUT /hc/{id}/campos` | Reemplaza el catálogo completo de la vacante |

`GET /jobforms/{token}` los devuelve en `camposOpcionales` —solo los activos— para que el
formulario sepa qué pintar antes de mostrarse. Es reemplazo y no edición campo por campo porque el
analista los define como un conjunto al abrir la vacante. Desactivar no es borrar: la respuesta de
quien ya lo contestó sigue teniendo sentido.

**Límite del CV.** `Cv:TamanoMaximoMb` (10 por defecto) se aplica en dos lugares: el endpoint
rechaza con 413 antes de tocar el disco, y el almacenamiento vuelve a contar mientras copia. Lo
segundo es el control real —el largo declarado por quien sube el archivo es una promesa, no una
comprobación— y si se pasa a mitad de la copia, el archivo parcial se borra. Un CV huérfano en el
recurso compartido no tendría fila que lo referencie y la purga de la Regla 17 nunca lo encontraría.

**Escaneo antivirus.** Todo CV pasa por Windows Defender antes de quedar guardado (Sección 9.6.1).
Se eligió Defender porque ya viene con el servidor: no suma licencia ni servicio que mantener.

El archivo aterriza primero en `_cuarentena`, dentro de la carpeta de CVs pero fuera de lo que ven
los analistas; recién si el escaneo sale limpio se mueve a su lugar. Escribirlo en el destino y
escanearlo después dejaría una ventana —corta, pero real— con un archivo sin revisar al alcance de
todos. Un rechazo borra el archivo, así que no quedan restos.

```json
"Antivirus": { "Habilitado": true, "TimeoutSegundos": 60, "ExigirEscaneo": true }
```

`ExigirEscaneo` decide qué pasa cuando **no se puede** escanear —Defender apagado, no instalado, o
sin responder a tiempo—. En `true` el CV se rechaza: guardar sin escanear porque el escáner estaba
caído es justo el caso que el requisito quiere evitar, y un CV rechazado se puede volver a subir.
Ponerlo en `false` es una decisión explícita y queda en el log en cada archivo.

**Una trampa de `MpCmdRun`:** devuelve código 2 tanto cuando encuentra una amenaza como cuando no
pudo escanear. Con Defender apagado deja `WARN: Product/Feature disabled` en su log y `Failed with
hr = 0x80004005` en la salida. El escáner mira ese marcador para distinguir los dos casos — sin
eso, un antivirus caído se tomaría por un archivo infectado, o al revés.

El Apps Script debe hacer un POST con este cuerpo:

```json
{
  "token": "el token del enlace",
  "dni": "45678912",
  "nombreCompleto": "Maria Quispe",
  "telefonoE164": "+51987654321",
  "email": "maria@correo.pe",
  "datosJson": "{\"experiencia\":\"2 anos\"}",
  "cvUrl": "https://drive.google.com/file/...",
  "consentimientoAceptado": true
}
```

Mientras el formulario viva en Google Forms, el CV queda en Drive y `cvUrl` es su enlace. La purga
de la Regla 17 limpia entonces la referencia pero **no puede borrar el archivo en el origen**: eso
queda como paso manual hasta migrar a Razor Pages, y el Worker lo registra en el log cuando ocurre.

### El Apps Script del formulario

Mientras el JobForms viva en Google Forms, lo que conecta el formulario con el sistema es un script
de Google: `scripts/apps-script/Codigo.gs`. Se instala una vez por formulario.

**1. La pregunta del token.** Agregá al formulario una pregunta de respuesta corta llamada `token`.
Es lo que identifica de qué postulación es cada envío; sin ella el webhook no puede atribuirlo a
nadie.

**2. El enlace de la vacante.** En el formulario, ⋮ → *Obtener vínculo prellenado*: escribí
cualquier cosa en `token`, copiá el enlace y reemplazá ese valor por `{token}`. Queda así:

```
https://docs.google.com/forms/d/e/FORM_ID/viewform?usp=pp_url&entry.123456789={token}
```

Ese es el enlace que se carga en la vacante, desde la pantalla **Vacantes**. El bot reemplaza
`{token}` por el token de cada postulante. **Sin el marcador no funciona con Google**: el sistema
agregaría `?t=…`, que Google ignora, y el token nunca llegaría a la respuesta (V27).

**3. El script.** Extensiones → Apps Script, pegar `Codigo.gs`, y en *Configuración del proyecto →
Propiedades del script*:

| Propiedad | Valor |
|---|---|
| `URL_WEBHOOK` | `https://…/jobforms/webhook-google` |
| `SECRETO` | el mismo valor de `JobForms__SecretoWebhook` |

**4. El disparador.** Disparadores → *Agregar disparador*: función `alEnviarFormulario`, evento *Al
enviarse el formulario*. Tiene que ser instalable, porque el envío sale a la red.

**5. Comprobarlo.** Ejecutá `probarConfiguracion()` una vez: manda un token inexistente y espera que
la Api lo rechace por eso. Si dice *URL y secreto OK*, quedó bien; un 401 es el secreto mal copiado.

**Reintentos.** El script reintenta cinco veces ante fallas pasajeras, y no reintenta un rechazo del
negocio, que daría siempre lo mismo. Un `429` (límite de velocidad, COR-15) tampoco es un rechazo
del negocio: se reintenta respetando `Retry-After` si la Api lo manda, o si no con el mismo backoff
exponencial que las demás fallas pasajeras, topado en 60 s por intento —el límite de ejecución de
un disparador instalable es de 6 minutos, y `Utilities.sleep` no acepta más de 5—. Repetir un envío
es seguro: la Api reconoce el que ya recibió, responde `yaRecibido` y no guarda otra respuesta ni le
vuelve a escribir al postulante (V27).

**Sin Google.** `scripts/probar-jobforms-local.ps1` manda el mismo cuerpo que el script, con el
token de una invitación real:

```powershell
.\scripts\probar-jobforms-local.ps1 -Token 6f1e6d0c2b5f4a1e8f0d9c3b7a2e5d41 -Dni 45678912
```

**Los adjuntos quedan en Drive**, no en el recurso compartido: lo que viaja es el enlace. Hay que
darles acceso a los analistas, y la purga de la Regla 17 puede limpiar la referencia pero no el
archivo. Se resuelve al migrar el formulario a Razor Pages (D3).

### SQL Express y AUTO_CLOSE

SQL Server Express crea las bases con `AUTO_CLOSE` activado. Con esa opción la base se apaga cuando
se cierra la última conexión, y cada reconexión tiene que volver a levantarla. Bajo el pool de
conexiones de la Api y el Worker eso se convierte en varias conexiones llegando a la vez, encolando
y expirando con un *timeout previo al inicio de sesión*. El síntoma es desconcertante: el sistema
funciona, después "a veces no responde", y `sqlcmd` tampoco entra hasta que se apagan los procesos.

La migración `DesactivarAutoClose` lo apaga en cualquier ambiente donde se aplique. Para
verificarlo a mano:

```bash
sqlcmd -S ".\SQLEXPRESS" -E -C -Q "SELECT name, is_auto_close_on FROM sys.databases WHERE name='RRHH_WhatsApp';"
```

Debe devolver `0`. Si devuelve `1`, la migración no se aplicó en esa base.

### Respaldos coordinados

Los datos del postulante viven en dos lugares: las filas en SQL Server y los archivos en el
recurso compartido. Respaldarlos por separado deja restauraciones inconsistentes, así que hay tres
scripts en `scripts/`:

| Script | Qué hace |
|---|---|
| `respaldo.ps1` | Respalda base y CVs como un conjunto, con manifiesto que los empareja |
| `verificar-respaldo.ps1` | Compara filas contra archivos y reporta lo que no coincide |
| `restaurar.ps1` | Restaura un conjunto completo y verifica al terminar |

```powershell
.\scripts\respaldo.ps1 -Destino D:\Respaldos\RRHH -CarpetaCv \servidor\rrhh\cv
```

**El orden no es arbitrario: primero la base, después los archivos.** El circuito del JobForms
escribe el CV al disco *antes* de confirmar la fila que lo referencia. Con ese orden, toda fila del
respaldo ya tiene su archivo en disco y la copia posterior lo captura. Al revés, una fila insertada
entre ambos pasos apuntaría a un archivo que el respaldo no tiene.

`verificar-respaldo.ps1` detecta las dos formas de desincronización, y devuelve código distinto de
cero para que una tarea programada lo note sin leer la salida:

- **Referencias rotas** — la fila existe, el archivo no. El postulante cree que entregó su CV.
- **Archivos huérfanos** — el archivo existe, ninguna fila lo referencia. Peor de lo que parece:
  la purga de la Regla 17 recorre `JobFormsRespuestas`, así que nunca los va a encontrar. Son
  datos personales que sobreviven a su plazo de retención.

**Dos cosas que muerden en el servidor.** `BACKUP DATABASE` corre como la cuenta del servicio de
SQL Server, no como quien ejecuta el script, así que esa cuenta necesita permiso de escritura sobre
la carpeta de destino — si no, sale `Cannot open backup device ... error 5`. Y **Express no soporta
compresión de respaldo**: el script consulta la edición y la omite solo, pero conviene saberlo al
dimensionar el disco. Como Express tampoco trae SQL Agent, la programación va por el Programador de
tareas de Windows.
### Vigilar que el Worker siga vivo

Las reglas por tiempo —escalamiento de 2h, recordatorio de 24h, aviso de 48h, archivado de 90
días— dependen enteramente del Worker, y un proceso detenido no avisa: simplemente deja de pasar
lo que tenía que pasar.

Cada bucle deja su señal de vida en `LatidosServicio` al cerrar un ciclo, con **su propia
tolerancia**: el consumidor de la outbox late cada 5 segundos y la purga cada 24 horas, así que un
umbral único daría falsas alarmas en uno o silencio en el otro. Quien late declara cuánto puede
tardar el próximo; la Api solo compara.

| Ruta | Qué mira | Para quién |
|---|---|---|
| `GET /health/vivo` | solo que el proceso responde | IIS, para decidir si reciclar el sitio |
| `GET /health` | SQL Server, los bucles del Worker y qué instancia tiene el candado | el monitoreo |

`/health` devuelve **503 con el detalle de qué bucle se detuvo y hace cuánto**, más lo que hizo en
su último ciclo:

```json
{
  "estado": "Unhealthy",
  "chequeos": {
    "base": { "estado": "Healthy" },
    "worker": {
      "estado": "Unhealthy",
      "descripcion": "Hay bucles del Worker detenidos: BarridoTiempo: 364 min sin latir"
    }
  }
}
```

`/health/vivo` no mira la base a propósito: si lo hiciera, una caída de SQL Server haría que IIS
reciclara la Api en bucle sin arreglar nada.

**Falta la alerta.** El endpoint se pone en rojo, pero **nadie lo está mirando**: hace falta un
monitor externo que lo consulte cada pocos minutos y avise. Cualquier cosa sirve —una tarea
programada con `curl`, o el monitoreo que ya use Sistemas—; lo que no sirve es suponer que un
endpoint en rojo alcanza por sí solo.

### Velocidad de envío

`LimitadorEnvio` (Sección 9.6.4) es una ventana deslizante de un segundo delante de cada adaptador:
si se llena, el mensaje espera en vez de descartarse o de saltarse el tope. El volumen saliente sin
control fue una de las causas del bloqueo original, así que esto no es una optimización — es una
guarda que se aplica **por proceso emisor** (con ARQ-03 el único emisor del bot es el Worker; la
Api solo despacha respuestas humanas de los analistas).

El tope no se fija al arrancar: `ProveedorParametrosEnvio` lo lee de `envio.maximo_por_segundo` en
`ConfiguracionReglas` antes de cada turno, con una caché de 30 s (constante técnica, no de
negocio). Un cambio del parámetro desde la administración llega al límite sin reiniciar nada: en la
Api en hasta 30 s, y en el Worker en hasta un minuto, porque ahí también vence la caché de 30 s de
`ConfiguracionReglasService`, que solo se invalida en el proceso que guardó el cambio. `MetaCloud:MaximoPorSegundo` y `Dialog360:MaximoPorSegundo` en
`appsettings.json` quedan solo como respaldo: se usan si la base no tiene el parámetro, no es un
número válido, o no se puede leer (una caída de SQL Server nunca frena un envío — se sigue con el
último tope conocido y se registra un warning, sin volver a golpear la base antes de que expire la
caché).

### Reintento de envíos salientes

Un error pasajero de Meta —un 503, un corte de red— dejaba al postulante sin la respuesta del bot,
y nada volvía a intentarlo. El reintento corre en el Worker y se apoya en clasificar el fallo:

| Clase | Cuándo | Qué hace |
|---|---|---|
| **Transitorio** | 5xx, 429, fallo de conexión (DNS, TCP o TLS: la petición nunca salió) | Reintenta con retroceso exponencial (1m, 2m, 4m), hasta agotar `envio.reintentos_maximos` |
| **Permanente** | 401, 403, 400, plantilla sin aprobar | No reintenta: daría el mismo resultado y solo gasta cuota |
| **Ambiguo** | Se perdió la respuesta (timeout, u otra falla de red después de que la petición ya viajó) | **Nunca reintenta solo.** La Cloud API no admite clave de idempotencia, así que el mensaje pudo haber salido y reenviarlo lo duplicaría |

Los adaptadores (`MetaCloudProvider`, `Dialog360Provider`) no llevan capa de resiliencia HTTP
propia: no hay `AddStandardResilienceHandler()` ni reintento implícito en el `HttpClient`. Esa capa
reintentaba POSTs por su cuenta, saltaba el `LimitadorEnvio` y se sumaba a lo de acá — el patrón que
costó la línea (C4/ARQ-04, decisión V29). El único reintento del sistema es este.

Dos decisiones que sostienen que esto sea seguro:

**Se reintenta el mensaje, no el evento de la outbox.** Reprocesar el evento volvería a correr todas
las reglas —reasignar, reauditar, reenviar lo que sí había salido—, que es justo el camino a
duplicar mensajes.

**Antes de cada reintento se revalida la Regla 15.** La ventana de 24h pudo cerrarse entre un
intento y otro: un texto libre que era legal hace tres horas ya no lo es. Si se cerró, el reintento
se abandona con el motivo escrito, no se fuerza el envío.

Los parámetros con los que se armó una plantilla se guardan en el mensaje: el contenido almacenado
conserva los `{{n}}` sin reemplazar, así que sin ellos el reintento no podría reconstruirla.

### Un solo Worker procesa

`EventosSistema` no tiene reserva por fila, así que dos Workers leyendo la cola tomarían el mismo
evento y podrían enviar dos veces el mismo mensaje de WhatsApp — justo lo que el proyecto existe
para evitar. Ya pasó que quedaran dos corriendo sin que nadie lo notara.

Por eso el Worker toma un **candado en SQL Server** (`sp_getapplock`) antes de arrancar sus bucles:

- **El primero lo toma y trabaja.** Un segundo Worker —en la misma máquina o en otra— lo encuentra
  tomado, lo dice en el log y **queda en espera sin tocar la cola**.
- **Si el activo muere, el de reserva toma el relevo** en hasta `EsperaCandadoSegundos` (15 por
  defecto). SQL Server suelta el candado solo al cerrarse la sesión, así que un proceso caído no lo
  deja tomado.
- **Si el activo pierde la conexión con la base, se detiene** con código de salida 1: sin conexión
  ya no tiene el candado, y otro podría tomarlo. Como servicio de Windows, conviene configurar el
  reinicio ante fallas para que vuelva y espere su turno.

`GET /health` muestra en `InstanciaActiva` qué máquina y qué proceso tiene el candado.

**Lo que no cubre:** entre que la conexión se cae y el activo lo nota pasan hasta
`VerificacionCandadoSegundos` (5 por defecto), más lo que tarde el lote en curso. Si en esa ventana
otro toma el candado, pueden solaparse. Para correr varias instancias a propósito sigue haciendo
falta la reserva por fila. La cadencia se ajusta en la sección `Worker` de `appsettings.json`.

Las pruebas contra SQL Server real —el candado, y el cambio de titular contra el índice único— se
omiten si no se indica cuál. No dejan filas: el candado usa nombres propios, y la otra corre dentro
de una transacción que se deshace.

```powershell
$env:RRHH_PRUEBAS_SQL = "Server=.\SQLEXPRESS;Database=RRHH_WhatsApp;Trusted_Connection=True;TrustServerCertificate=True"
dotnet test --filter "FullyQualifiedName~SqlServer"
```

Para levantarlo en desarrollo, con la Api corriendo aparte:

```bash
dotnet run --project src/RRHH.WhatsApp.Worker
```

## Despliegue en IIS

Tres procesos: la **Api** y la **bandeja** como sitios de IIS, el **Worker** como servicio de
Windows. Los tres salen de `scripts/publicar.ps1`, que deja una carpeta por proceso.

**En el servidor, una sola vez:**

- **.NET Hosting Bundle**, de la misma versión mayor que el proyecto. Es lo que instala el módulo de
  ASP.NET Core en IIS; sin él, el sitio responde 500.19 y no arranca de ninguna forma.
- **WebSocket Protocol** habilitado en IIS. Por ahí van Blazor Server y el canal en vivo de la
  Sección 9.6.3.
- Una **cuenta de servicio** de dominio para el pool de IIS y para el Worker, con permisos en SQL
  Server y en la carpeta de CVs.

```powershell
.\scripts\publicar.ps1 -Destino D:\Publicado\RRHH
```

```powershell
.\scripts\instalar-iis.ps1 -RutaApi D:\Publicado\RRHH\api -RutaBandeja D:\Publicado\RRHH\bandeja -Cuenta 'DOMINIO\svc_rrhh'
```

```powershell
.\scripts\instalar-servicio-worker.ps1 -RutaPublicada D:\Publicado\RRHH\worker -Cuenta 'DOMINIO\svc_rrhh'
```

Los dos instaladores exigen administrador y se pueden volver a correr: actualizan lo que ya existe en
vez de duplicarlo, que es lo que hace falta en cada nueva versión.

**Los secretos no se publican.** Van como variables de entorno **de máquina** (`setx /M`), que es lo
que leen los tres procesos:

| Variable | Para qué |
|---|---|
| `ConnectionStrings__RrhhWhatsApp` | La base. La misma para los tres procesos |
| `Jwt__Clave` | Firma de los tokens, mínimo 32 caracteres. Sin ella la Api no arranca |
| `Arranque__EmailSistemas` | Correo del primer analista de Sistemas (V25) |
| `MetaCloud__*` o `Dialog360__*` | Credenciales del proveedor de WhatsApp |
| `JobForms__SecretoWebhook` | Lo que autentica al Apps Script de Google |
| `Cv__Carpeta` | Ruta UNC del recurso compartido de CVs |
| `ASPNETCORE_ENVIRONMENT` | `Production` |

Se leen al arrancar: después de cambiarlas hay que reciclar el pool y reiniciar el servicio.

**Lo que el script deja configurado, y por qué.** El pool va sin CLR administrado —.NET Core no corre
en ese pipeline—, **sin apagado por inactividad y sin reciclado por tiempo**: cualquiera de los dos
cortaría los circuitos de Blazor y el canal en vivo en medio de una conversación. El servicio del
Worker queda con reinicio ante fallas, y con `failureflag` prendido para que la recuperación valga
también cuando el proceso termina con código distinto de cero — que es justo como el Worker avisa que
perdió el candado (V24).

**Lo que queda a mano:** el binding HTTPS con el certificado de la empresa sobre el sitio de la Api
—es la URL que ve Meta—, y `Api:BaseUrl` en el `appsettings.json` de la bandeja apuntando al sitio de
la Api.

**Para comprobar que quedó bien:** `GET /health` en verde —base y bucles del Worker—, y entrar a la
bandeja. `GET /health/vivo` es el que conviene dejarle a IIS como prueba de estado: no mira la base,
así que una caída de SQL Server no lo hace reciclar el sitio en bucle.

## Antes de salir a producción

- [ ] WABA verificado y aprobado por Meta
- [ ] Las 6 plantillas de `Plantillas` registradas y aprobadas en Meta, y marcadas `Activa = true`
- [ ] URL pública HTTPS con certificado válido para el webhook
- [ ] Cadena de conexión y credenciales de 360dialog fuera del código
- [ ] .NET Hosting Bundle instalado en el servidor y WebSocket Protocol habilitado en IIS
- [ ] Sitios creados con `scripts/instalar-iis.ps1` y Worker instalado con `scripts/instalar-servicio-worker.ps1`, con la cuenta de servicio
- [ ] Variables de entorno de máquina definidas (tabla de *Despliegue en IIS*), con `ASPNETCORE_ENVIRONMENT=Production`
- [ ] `Api:BaseUrl` de la bandeja apuntando al sitio de la Api, y binding HTTPS con certificado sobre el sitio de la Api
- [ ] Cuentas, analistas, respaldos por cuenta y horario de atención cargados (`GET /cuentas` los muestra)
- [ ] `AUTO_CLOSE` desactivado en la base de producción (lo hace la migración `DesactivarAutoClose`)
- [ ] Aviso de privacidad revisado por Legal y su versión registrada en `ConfiguracionReglas`
- [ ] `HC.UrlJobForms` cargada en cada vacante abierta, y `JobForms__SecretoWebhook` desplegado en el Apps Script
- [ ] Carpeta de CVs (`Cv:Carpeta`) creada en el recurso compartido, con permisos para la cuenta del Worker
- [ ] Windows Defender activo en el servidor (`Get-MpComputerStatus`), o `Cv:Antivirus:ExigirEscaneo` bajado a conciencia
- [ ] Plazo de retención de CVs (`datos.retencion_cv_dias`) confirmado con Legal
- [ ] `Jwt__Clave` desplegada como variable de entorno (mínimo 32 caracteres) — sin ella la Api no arranca
- [ ] `Arranque__EmailSistemas` definido y `POST /sesion/arranque` ejecutado apenas publicado: en una base nueva crea el analista de Sistemas y le fija la contraseña
- [ ] Quien mira el panel de métricas dado de alta con `"rol":"Jefatura"`, y su contraseña restablecida por Sistemas
- [ ] Monitor externo consultando `GET /health` y avisando cuando devuelva 503 (Sección 9.6.2)
- [ ] `scripts/respaldo.ps1` programado en el Programador de tareas, con permiso de escritura para la cuenta del servicio de SQL Server
- [ ] `scripts/verificar-respaldo.ps1` programado como chequeo periódico de consistencia
- [ ] Una restauración de prueba hecha al menos una vez, con `restaurar.ps1`, antes de confiar en los respaldos
