# Contexto del proyecto

Sistema interno de RRHH que reemplaza el uso de WhatsApp Business desde múltiples PCs por la
WhatsApp Business API oficial. 13 analistas, ~20 cuentas/clientes, ~16,000 interacciones al mes.

**Lee siempre antes de trabajar:**

- `README.md` — estructura, estado por componente y checklist de producción.
- `docs/decisiones.md` — decisiones tomadas y desviaciones respecto del dossier, con su porqué.
- `docs/Dossier_Maestro_WhatsApp_RRHH_v2.docx` — requisitos y las 20 reglas de negocio. Es la
  fuente de verdad funcional. La versión `_v2` es la vigente.
- `docs/auditoria/04-documento-continuidad.md` — backlog vigente y protocolo de trabajo.

## Stack

.NET 10 · ASP.NET Core · EF Core 10 · SQL Server (local: `.\SQLEXPRESS`, base `RRHH_WhatsApp`) ·
Blazor Server · 360dialog como BSP · despliegue on-premise sobre IIS.

## Límites entre proyectos

No los cruces sin pasar por el agente `arquitecto`.

```
Domain          → no referencia a nadie
Contracts       → no referencia a nadie (solo DTOs)
Application     → Domain, Contracts
Infrastructure  → Application, Domain
Reporting       → Domain, Contracts
Api             → Application, Infrastructure, Reporting, Contracts
Worker          → Application, Infrastructure
Frontend        → Contracts únicamente; habla con la Api por HTTP, nunca toca SQL Server
```

## Agentes por rol

Cada superficie del sistema tiene un dueño. Antes de trabajar, usa el que corresponda; si el
trabajo cruza dos, empieza por `arquitecto`.

| Agente | Se ocupa de | Ejemplo de encargo |
|---|---|---|
| `arquitecto` | Diseño, límites entre módulos, `docs/decisiones.md` | "¿Este campo va en Conversacion o en Postulacion?" |
| `reglas-negocio` | Las 20 reglas como `IReglaNegocio`, motor y `ContextoRegla` | "Implementa la Regla 9" |
| `backend-datos` | Esquema, migraciones, servicios de dominio, endpoints, Worker | "Agrega el endpoint de transferencias" |
| `integracion-whatsapp` | 360dialog y Meta: adaptador, webhook, firma, idempotencia, plantillas | "Conecta el proveedor" |
| `jobforms-datos` | Formulario de postulación, CVs, datos personales (Reglas 9, 17, 20) | "Arma el webhook de Google Forms" |
| `bandeja-blazor` | La pantalla del analista: chat, kanban, buscador, tiempo real | "Construye el tablero kanban" |
| `reporting-metricas` | Panel de gerencia y el modelo de solo lectura (Regla 18) | "Calcula el tiempo de primera respuesta" |
| `despliegue-operacion` | IIS, secretos, migraciones entre ambientes, health, backups | "Prepara el despliegue" |

Reparto de responsabilidades que se cruzan a menudo:

- Una **regla** decide; un **servicio** ejecuta. `reglas-negocio` devuelve `AccionRegla`,
  `backend-datos` la ejecuta.
- El **envío** de un mensaje es de `integracion-whatsapp`; **cuándo y qué** enviar es de
  `reglas-negocio`.
- Las **tablas** del JobForms las define `backend-datos`; **lo que se guarda y por cuánto tiempo**
  es de `jobforms-datos`.

## Convenciones

- **Identificadores en español sin tildes**: `ConversacionId`, `FechaEnvio`, `MoverEtapaKanban`.
- **Fechas en UTC** en base de datos. Se muestran en `America/Lima` (UTC-5, sin horario de verano).
- **Las reglas deciden, no ejecutan.** Cada `IReglaNegocio` devuelve `AccionRegla`; la capa de
  aplicación las ejecuta. Es lo que permite probarlas sin base de datos ni proveedor.
- **Nada de literales de tiempo en el código.** Las 2 horas, los 3 días, los 90 días y los topes
  de envío viven en `ConfiguracionReglas` y se leen vía `ContextoRegla.ConfigInt/ConfigBool`.
- **Comentarios que expliquen el porqué**, no el qué, y siempre atados a la regla o sección del
  dossier que justifican la decisión. Sigue la densidad y el tono del código ya escrito.
- **Una regla por tiempo mira el hilo o el proceso** (V37). `TiempoTranscurrido` evalúa una vez por
  conversación y `TiempoTranscurridoPostulacion`, una vez por postulación. Con el disparador
  equivocado, la regla se repite por cada proceso o no ve ninguno.
- **El orden de las reglas vive en un solo lugar:** `AgregarReglas` de
  `Infrastructure/RegistroDependencias.cs`, en orden de `Prioridad`. Una regla nueva es una línea
  ahí y una prioridad que no choque con ninguna: `RegistroDependenciasTests` falla si dos comparten
  prioridad, si el motor no las recibe todas, o si el entorno de pruebas quedó desalineado.
- **Un `Conversacion` es el hilo de WhatsApp** (uno por número de teléfono). Un `Postulacion` es
  el proceso (persona + vacante) y es lo que recorre el kanban. No los mezcles: la razón está en
  `docs/decisiones.md`, desviación V1.

## Reglas que gobiernan todo lo que envía mensajes

El proyecto existe porque a Meta le bloquearon la línea. Antes de escribir cualquier código que
produzca un mensaje saliente:

- **Regla 15** — sin opt-in registrado en `Conversacion.FechaOptIn` no se envía nada, ni con
  plantilla. Fuera de la ventana de 24h (medida desde el último mensaje **entrante**) solo se
  puede enviar plantilla aprobada por Meta.
- **Plantillas** — las 6 sembradas están `Activa = false` a propósito, hasta que Meta las apruebe.
  No las actives desde código ni desde una migración.
- **Velocidad de envío** — el adaptador limita el saliente (`envio.maximo_por_segundo`). No lo
  puentees.
- **El bot no envía: encola** (V29). Las acciones de envío se registran en `Mensajes` como `EnCola`
  con `ClaveIdempotencia` y las manda `DespachoEnvios`. Nada fuera de `DespachoEnvios` y de la
  respuesta del analista llama a `IWhatsAppProvider` para enviar, y no hay reintentos HTTP implícitos.
- **Dentro de la ventana, el bot habla en texto** (P1, COR-03). Los mensajes del bot son
  `EnviarMensajeBot(texto, clavePlantilla, parametros)`: con la ventana de 24h abierta sale el
  texto, cerrada sale la plantilla activa, y sin ninguna de las dos no sale nada y queda una
  `AlertaOperativa`. Los textos viven en `Application/Reglas/TextosBot.cs`, en paralelo a los
  borradores de `DatosSemilla`: si cambia uno, cambia el otro. Lo que sella un envío
  (`SellarConversacion`, `SellarPostulacion`, `MarcarRecordatorioJobForms`) lleva
  `SoloSiSeEnvioAnterior = true`, porque sellar algo que no salió lo pierde para siempre.

## Verificación

Todo cambio debe dejar esto en verde antes de darse por terminado:

```bash
dotnet build --nologo
```

```bash
dotnet test --nologo
```

Cambios de esquema:

```bash
dotnet ef migrations add NombreDelCambio --project src/RRHH.WhatsApp.Infrastructure --output-dir Persistencia/Migraciones
```

No edites migraciones ya aplicadas: agrega una nueva.
