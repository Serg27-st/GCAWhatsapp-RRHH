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

## Riesgos abiertos

| Riesgo | Detalle | Mitigación |
|---|---|---|
| **Login de Google para subir el CV** | Google Forms exige que el postulante inicie sesión con cuenta Google para adjuntar archivos. En reclutamiento masivo eso tumba parte del embudo. | Medir la caída en el piloto. Si es alta, adelantar la migración a Razor Pages (D3). |
| **Regla 17 sin trazabilidad por versión** | Con Google Forms se sella la versión vigente al momento del envío, no la que el postulante realmente vio. | `VersionAvisoPrivacidad` ya existe en el modelo; se llena bien al migrar a Razor Pages. |
| **Regla 20 manual** | El analista debe desactivar el formulario de Google a mano al cerrar la vacante. | `PATCH /hc/{id}/cerrar` deja el estado correcto en el sistema; el bot ya filtra vacantes cerradas antes de enviar el link. |
| **Webhook público** | 360dialog necesita una URL HTTPS con certificado válido. On-premise implica DNS, certificado y regla de firewall, con plazo propio. | Iniciarlo en paralelo al desarrollo, como la aprobación del WABA. |
| **SQL Server Express** | 10 GB por base y sin SQL Agent. | Suficiente para el volumen actual; los CVs van fuera de la BD y el Worker reemplaza al Agent. Confirmar la instancia de producción. |
| **Aprobación del WABA** | Es el cuello de botella real del proyecto, no el desarrollo. | Iniciar el trámite desde el día 1 (Sección 10 del dossier). |

## Convenciones

- **Identificadores en español sin tildes** (`ConversacionId`, `FechaEnvio`). Nombres de tabla en
  español con la forma del dossier.
- **Fechas en UTC** en base de datos; se muestran en `America/Lima` (UTC-5, sin horario de verano).
- **Las reglas deciden, no ejecutan.** Cada `IReglaNegocio` devuelve `AccionRegla`; la capa de
  aplicación las ejecuta. Es lo que permite probarlas sin base de datos ni proveedor.
- **Parámetros en `ConfiguracionReglas`**, nunca literales en el código: 2 horas, 3 días, 90 días,
  topes de envío.
