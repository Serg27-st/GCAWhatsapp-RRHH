# Auditoría — Fase 2: el código contra la arquitectura de referencia

> **Qué es este documento.** El resultado de medir el repositorio contra
> `docs/auditoria/01-arquitectura-funcional.md`, incluidas las resoluciones A1–A15 y los principios
> P1–P4 validados el 2026-09-14 (criterio: atención preferente al postulante).
>
> **Base auditada**
>
> - Rama `master`, commit `862ac92`, **más 89 archivos modificados sin commitear**: se auditó el
>   árbol de trabajo tal cual está.
> - `dotnet build`: **0 errores y 0 advertencias**.
> - `dotnet test`: **349 superadas, 0 fallidas, 4 omitidas**. Las omitidas son las de SQL Server, que
>   necesitan `RRHH_PRUEBAS_SQL`.
>
> **Método.** Lectura completa de dominio, reglas, casos de uso, servicios, persistencia, proveedor,
> Worker, Api, Reporting y las pantallas principales. Cada hallazgo se contrastó con el código (se
> citan archivo y líneas) y, cuando correspondía, con las pruebas que deberían haberlo detectado.
>
> **Clasificación**
>
> | Marca | Significado |
> |---|---|
> | ✅ | Conforme |
> | ⚠️ | Parcial o frágil |
> | ❌ | Ausente o roto |
> | 🔀 | Desviación no documentada |
>
> | Severidad | Criterio |
> |---|---|
> | **Crítico** | Rompe el flujo del postulante o arriesga otro bloqueo de Meta |
> | **Alto** | Una regla no se cumple en un caso frecuente |
> | **Medio** | Degrada el servicio o la operación |
> | **Bajo** | Higiene de código |
>
> **Nomenclatura.** Los hallazgos son **C#** (crítico), **AL#** (alto), **M#** (medio) y **B#**
> (bajo). **A#** y **P#** remiten a las resoluciones y los principios de la Fase 1 §5.

---

## 1. Resumen ejecutivo

**La arquitectura está bien construida; el flujo del postulante, no.**

### Lo que está sólido

- Los límites entre proyectos se respetan sin una sola violación.
- Las reglas deciden y no ejecutan: el patrón Strategy está bien aplicado.
- La seguridad se diseñó en capas: autorización por defecto, control por objeto, PBKDF2, firma del
  webhook en tiempo constante y secretos fuera del código.
- Hay un solo Worker activo gracias al candado de SQL Server, con latidos y health.
- La documentación y los comentarios son de una calidad poco común.

### Lo que falla

Al seguir el recorrido real de un mensaje de punta a punta, varias piezas correctas por separado
fallan al combinarse. Las pruebas no lo detectan porque evalúan cada regla con un contexto armado a
mano, o un solo mensaje por flujo:

1. **El aviso fuera de horario (R3) nunca sale en producción** (C1).
2. **El bot reenvía el menú de vacantes con cada mensaje** del postulante en cuentas con más de una
   vacante (C2). Es spam hacia el postulante: la causa de bloqueo de la §2.4.
3. **Mientras Meta no apruebe las plantillas, el bot no confirma el formulario ni avisa la vacante
   cerrada** (C3), aunque dentro de la ventana podría hacerlo en texto libre (P1).
4. **Tres mecanismos pueden duplicar o perder mensajes** (C4, C5, C6): la resiliencia HTTP
   reintenta envíos por su cuenta, un fallo parcial reprocesa el evento completo, y la ingesta no
   es atómica.
5. **Hay postulantes que quedan sin dueño o invisibles:**
   - «Sin clasificar» no se puede tomar (AL1).
   - El contador del menú manda a «Sin clasificar» sin reintento (AL2).
   - La repregunta le quita el analista a quien está en proceso (AL3).
   - Quien vuelve tras el archivado queda oculto (AL10).

**Conteo:** 6 críticos · 10 altos · 11 medios · 9 bajos.

### Estado por eje

| Eje (Fase 1 §6) | Estado | Resumen |
|---|---|---|
| 1. Límites entre proyectos | ✅ | Coinciden exactamente con la tabla de `CLAUDE.md` |
| 2. Modelo EF y migraciones | ⚠️ | Completo y bien indexado. Faltan campos de A2, A6, A8 y A11; un estado nunca se usa; invariantes solo en servicio |
| 3. Reglas y pruebas | ❌ | R3 inoperante; R9 con dos defectos críticos; R14 y R16 con huecos |
| 4. Guardia R15 en todo envío | ⚠️ | Opt-in y ventana bien en todos los caminos; la velocidad está desconectada y se puentea (AL8, C4) |
| 5. Autorización | ✅ / ⚠️ | Sólida. Hueco en «Sin clasificar» (AL1) y sin revocación de JWT (M10) |
| 6. Outbox, idempotencia, concurrencia | ❌ | C4, C5, C6 |
| 7. Worker | ⚠️ | Candado y latidos ✅. Faltan reglas por tiempo (A1, A9, A12, A14) |
| 8. Frontend | ⚠️ | Faltan tomar hilo, estado de entrega y adjuntos |
| 9. Calidad y deuda | ⚠️ | Código limpio; pruebas sin escenarios multi-mensaje y sobre EF InMemory |
| 10. README contra código | 🔀 | Cuatro afirmaciones no se sostienen (§8) |

---

## 2. Conformidad arquitectónica

### 2.1 Referencias entre proyectos

Todas conformes. Ninguna referencia sobra ni falta.

| Proyecto | Referencia | Esperado | Estado |
|---|---|---|---|
| Domain | — | nadie | ✅ |
| Contracts | — | nadie | ✅ |
| Application | Domain, Contracts | Domain, Contracts | ✅ |
| Infrastructure | Application, Domain | Application, Domain | ✅ |
| Reporting | Domain, Contracts | Domain, Contracts | ✅ |
| Api | Application, Infrastructure, Reporting, Contracts | ídem | ✅ |
| Worker | Application, Infrastructure | ídem | ✅ |
| Frontend | Contracts | Contracts | ✅ |

### 2.2 Capas y patrones

| Elemento (Fase 1 §4) | Implementación | Estado |
|---|---|---|
| Strategy (`IReglaNegocio`, `AccionRegla`, `ContextoRegla`) | `Domain/Reglas`, 13 clases en `Application/Reglas/Implementaciones` | ✅ |
| Motor (orden, detención) | `MotorReglas.cs`: una regla que lanza se registra y se sigue | ✅ |
| Ejecutor único de efectos | `EjecutorAcciones.cs` | ✅ (sin idempotencia: C5) |
| Fábrica de contexto | `FabricaContextoRegla.cs`, unas 12 consultas por evaluación (aceptable al volumen) | ⚠️ (AL2) |
| Adapter de proveedor | `IWhatsAppProvider` con Meta, 360dialog y simulado; cuerpos e intérprete compartidos | ✅ (C4) |
| Outbox | `EventosSistema` + `ConsumidorOutbox` + `DifusorNotificaciones` con tipos disjuntos | ⚠️ (C5, C6, M1) |
| Sin capa Repository (V6) | Servicios de dominio como frontera de módulo | ✅ |
| DTOs en Contracts | La Api mapea en `Mapeo/MapeoBandeja.cs`; el Frontend solo usa Contracts | ✅ |
| Filtro de acceso en la frontera HTTP (V22) | `FiltroAccesoConversacion` + `ResultadoAcceso` | ✅ |
| Reporting de solo lectura (V15) | `ReportingDbContext` con `NoTracking` | ✅ (M7) |
| Tiempo real (V16) | Hub en la Api, grupo por claim | ✅ |

**Observación de diseño.** El README dice que las reglas viven en Domain; están en Application. No
rompe ningún límite, pero conviene corregir el texto.

---

## 3. Modelo de datos contra la Fase 1 §3

### 3.1 Tablas

| Tabla | Estado | Observaciones |
|---|---|---|
| `Cuentas` | ✅ | Nombre único |
| `Analistas` | ✅ | Rol como entero, email único, hash inline |
| `AnalistaCuenta` | ✅ | Índices únicos filtrados de titular y respaldo (V17) |
| `Ausencias` | ✅ | Índice caliente (analista, inicio, fin) |
| `HorarioAtencion` | ✅ | — |
| `HC` | ⚠️ | `CodigoJobForms` sin uso. **Falta `CodigoAviso` único** (A6) |
| `HCCamposOpcionales` | ✅ | — |
| `Postulantes` | ✅ | DNI único |
| `Postulaciones` | ⚠️ | Único (postulante, HC) ✅. `EstadoPostulacion.Archivada` **nunca se asigna** (M6). Falta `Reingreso` (A2) |
| `EtapasKanban` | ⚠️ | El desenlace se deduce del **nombre** de la etapa (`StartsWith("Contratado")`). Falta `EstadoResultante` explícito (AL7) |
| `EstadosPostulanteCuenta` | ✅ | — |
| `Conversaciones` | ⚠️ | Campos de V1, V2, V4 y RowVersion ✅. Faltan `FechaAvisoFueraHorario` (A8), estado del menú, y un índice por `FechaUltimaActividad` para el archivado |
| `Mensajes` | ⚠️ | `ProviderMessageId` único filtrado ✅; reintentos ✅. Sin referencia a adjuntos (M3) |
| `Transferencias` | ⚠️ | Sin índice único filtrado «una pendiente por conversación» y sin vencimiento (M5) |
| `Plantillas` | ⚠️ | Semilla `Activa = false` ✅, pero la entidad trae `Activa = true` por defecto (M11) |
| `JobFormsInvitaciones` | ✅ | Token único |
| `JobFormsRespuestas` | ⚠️ | Sin índice único por `InvitacionId`; la idempotencia de V27 depende de una consulta previa (B9) |
| `EventosSistema` | ⚠️ | El índice (Estado, Fecha) no incluye `Tipo`; los procesados no se purgan; el payload guarda teléfono y texto del postulante (M1, AL6) |
| `ConfiguracionReglas` | ⚠️ | `horario.descripcion` se lee en R3 pero no está sembrada; `envio.maximo_por_segundo` está sembrada y nadie la lee (AL8) |
| `Auditoria` | ✅ | — |
| `LatidosServicio` | ✅ | — |

### 3.2 Invariantes

| # | Invariante | Estado |
|---|---|---|
| 1 | Un hilo por teléfono; una postulación por persona y vacante | ✅ Índices únicos |
| 2 | Un titular y como máximo un respaldo por cuenta | ✅ |
| 3 | Una transferencia pendiente por conversación | ⚠️ Solo chequeo en el servicio, con carrera posible |
| 4 | `ProviderMessageId` no se repite | ✅ |
| 5 | Ningún saliente sin opt-in | ✅ Motor, ejecutor, envío del analista y reintento |
| 6 | Fuera de ventana, solo plantilla activa | ✅ |
| 7 | El desenlace vive solo en `Postulacion.Estado` | ⚠️ No se revierte al sacar la tarjeta de la columna final (AL7) |
| 8 | Invitación completada con exactamente una respuesta | ⚠️ Sin índice; ingesta no atómica (C6) |
| 9 | Todo CV con fila, y viceversa | ⚠️ El camino propio deja el CV huérfano si la validación falla (M8) |

---

## 4. Reglas de negocio

| R | Estado | Qué hay | Hallazgos |
|---|---|---|---|
| 1 | ⚠️ | Asigna al titular solo si el hilo no tiene dueño | AL5, AL9 |
| 2 | ⚠️ | Horas hábiles, respaldo fijo, RowVersion | AL1, M4 (sin 2º nivel ni plazo para «Sin clasificar») |
| 3 | ❌ | Clase y prueba unitaria existen; nunca dispara en producción | **C1**, C3 |
| 4 | ✅ | `NivelAcceso` en la frontera, 404/403 | AL1 |
| 5 | ⚠️ | Consecuencia de R1 + R19 | AL1, AL5 |
| 6 / 11 | ⚠️ | Aviso genérico en asignación y detalle | AL4, AL9; prefijo multi-cuenta (A3) sin implementar |
| 7 | ✅ | Motivo obligatorio; cascada a postulaciones (V13) | — |
| 8 | ⚠️ | Una pendiente, urgente o con aceptación, avisos en vivo | M5 |
| 9 | ❌ | Enlace, recordatorio, aviso a 48 h, repregunta, confirmación | **C2**, **C3**, AL2, AL3, M8 |
| 10 | ✅ | El menú se identifica como asistente | — |
| 12 | ⚠️ | Se dispara por blacklist y por la columna Descartado | C3, AL7 (duplicado), sin opción «no enviar» (A11) |
| 13 | ⚠️ | Tablero, arrastre, RowVersion | AL7 |
| 14 | ⚠️ | Deriva al respaldo durante la ausencia | AL4 |
| 15 | ✅ / ⚠️ | Opt-in y ventana en todos los caminos | AL8, C4 (velocidad) |
| 16 | ⚠️ | Archiva la conversación si no hay nada vivo | M6, **AL10** |
| 17 | ⚠️ | Consentimiento, versión, purga, anonimización | AL6, M8 |
| 18 | ⚠️ | Primera respuesta humana con mediana, conversión, actividad | M7 |
| 19 | ⚠️ | Reintento y derivación | AL1, AL2, AL5, B6 |
| 20 | ✅ / ⚠️ | Bloquea el enlace y el formulario; vuelve a mostrar el menú | C3 (el aviso no sale sin plantilla) |

---

## 5. Hallazgos

### 5.1 Críticos

#### C1 — El aviso fuera de horario (R3) nunca se envía

- **Dónde:** `R03FueraDeHorario.cs:38-43` y `RecepcionWebhook.cs:91`.
- **Evidencia:**
  - El webhook ejecuta `RegistrarEntradaAsync`, que mueve `FechaUltimoMensajeEntrante` al mensaje
    actual, **antes** de encolar.
  - Cuando el Worker evalúa, `AhoraUtc - FechaUltimoMensajeEntrante` es la demora de proceso (unos
    segundos), siempre menor que las 8 h del chequeo. `yaAvisadoEnEstaTanda` es entonces siempre
    `true`.
  - `FabricaContextoRegla.cs:203-206` documenta exactamente este efecto para la R9, pero la R3 no lo
    contempla.
  - La prueba (`ReglasFlujoTests.cs:88+`) arma el contexto con una fecha pasada, así que no lo
    detecta.
- **Agravantes:**
  - 8 h es un literal de tiempo, prohibido por la convención.
  - La descripción del horario sale de la clave `horario.descripcion`, que no está sembrada, con un
    texto fijo de respaldo. Ignora `IHorarioAtencionService.DescribirHorarioAsync`.
- **Impacto:** el postulante que escribe de noche no recibe ninguna respuesta.
- **Referencia:** R3, A8, P1. Clase ❌.

#### C2 — El menú de vacantes se reenvía con cada mensaje del postulante

- **Dónde:** `R09EnvioLink.cs:36-41`.
- **Evidencia:**
  - Si la cuenta tiene más de un HC abierto y el mensaje no trae botón de vacante, `vacante` es nulo
    y la regla devuelve `MostrarMenuVacantes` **antes** de mirar `HcsConInvitacion`.
  - Con el contexto de cuenta ya fijado, cada pregunta del postulante («¿cuál es el sueldo?»),
    incluso después de completar el formulario, recibe otra vez el menú.
  - Lo mismo le pasa a toda conversación de una cuenta que abre una segunda vacante.
  - `R09EnvioLinkTests.cs:61-87` prueba solo el primer mensaje.
- **Impacto:** spam al postulante, que es la señal de reporte de la §2.4; el analista ve el hilo
  lleno de menús.
- **Referencia:** R9, P4. Clase ❌.

#### C3 — El bot queda mudo mientras las plantillas no están aprobadas

- **Dónde:**
  - `EjecutorAcciones.cs:148-164`.
  - Las reglas R03 (`:49-50`), R09 de confirmación (`:36-38`), R20 (`:39-41`) y R12.
- **Evidencia:**
  - Todas piden `EnviarPlantilla` aunque la ventana de 24 h esté abierta: el postulante acaba de
    escribir o de completar el formulario.
  - Con la plantilla inactiva (las 6 lo están, V8) el envío se omite y solo queda el evento
    `EnvioOmitidoSinPlantilla`, que **nadie consume** (M1).
  - `R09SeguimientoJobForms.cs:49-52` sella el recordatorio aunque se haya omitido, así que ese
    recordatorio se pierde para siempre.
- **Impacto:** tras completar el formulario, el postulante no recibe confirmación, y el aviso de
  vacante cerrada tampoco sale. Es el momento de mayor abandono del embudo.
- **Referencia:** P1, R9, R12, R20. Clase ❌.

#### C4 — La capa de resiliencia HTTP reintenta envíos por su cuenta

- **Dónde:** `RegistroDependencias.cs:140` y `:164` (`AddStandardResilienceHandler()`), junto con
  `MetaCloudProvider.cs:145-159`.
- **Evidencia:**
  - El handler estándar trae reintento (3), timeout por intento y circuit breaker. Por defecto
    reintenta **también POST** ante 5xx, 408, 429, `HttpRequestException` y timeout.
  - Esto anula el diseño de «Ambiguo no se reintenta». Un corte de conexión después de que Meta
    aceptó el mensaje se reenvía solo.
  - Los reintentos internos saltan el `LimitadorEnvio`: esperan turno una sola vez por envío lógico.
  - Se suman a los de `ReintentoEnvios`: hasta 4 × 4 intentos por mensaje.
  - `BrokenCircuitException` y `TimeoutRejectedException` no son `HttpRequestException` ni
    `TaskCanceledException`, así que atraviesan el proveedor sin clasificar y producen C5.
- **Impacto:** mensajes duplicados al postulante, el patrón que bloqueó la línea.
- **Referencia:** §9.6.4, RNF-02, RNF-03. Clase 🔀.

#### C5 — Un fallo parcial reprocesa el evento completo y duplica envíos

- **Dónde:** `ConsumidorOutbox.cs:130-150` y `EjecutorAcciones.cs:38-52`.
- **Evidencia:**
  - Las acciones se ejecutan una por una, cada una con su `SaveChanges`.
  - Si algo falla **después** de un envío (auditoría, marca de procesado, excepción de C4, caída de
    la base), `MarcarFallidoAsync` devuelve el evento a `Pendiente`.
  - El siguiente ciclo **vuelve a correr todas las reglas** y reenvía lo que ya salió.
  - El README afirma lo contrario («se reintenta el mensaje, no el evento»): eso vale para
    `ReintentoEnvios`, no para el consumidor.
- **Impacto:** duplicados de menús, enlaces y confirmaciones.
- **Referencia:** RNF-03, RNF-04. Clase ❌.

#### C6 — La ingesta no es atómica y puede perder mensajes del postulante

- **Dónde:** `RecepcionWebhook.cs:78-102` y `RecepcionJobForms.cs:76-92`.
- **Webhook:**
  - Guardar el mensaje, actualizar la conversación y publicar el evento son tres `SaveChanges`
    separados.
  - Si falla la publicación, Meta reintenta, el mensaje ya existe, se descarta como duplicado y
    **nunca se encola**.
- **JobForms:**
  - La invitación se marca `Completado` antes de publicar `JobFormsCompletado`.
  - Si la publicación falla, el reintento del Apps Script recibe `yaRecibido` y no se republica.
    Resultado: sin confirmación, sin asignación y sin aviso R6.
- **Impacto:** postulantes que escribieron o completaron el formulario sin que nadie los atienda
  jamás.
- **Referencia:** §8.1 (outbox sin pérdida), RNF-04. Clase ❌.

### 5.2 Altos

#### AL1 — «Sin clasificar» no tiene dueño posible

- **Dónde:** `ConversacionService.cs:74-76`, `:416-431` y `Bandeja.razor`.
- **Evidencia:**
  - En «Sin clasificar», cualquier analista tiene acceso total, pero **no existe una acción para
    tomar o clasificar** el hilo: ni endpoint ni botón.
  - Responder no asigna `AnalistaAtendiendoId` ni `CuentaContextoId`.
  - El hilo sigue visible para todos y varios analistas pueden escribirle a la vez, lo que viola la
    R11.
  - La R2 no lo vigila, porque el barrido solo mira estados `Activa`.
- **Impacto:** justo los postulantes que el bot no entendió quedan sin responsable y sin plazo.
- **Referencia:** R19, R11, P3. Clase ❌.

#### AL2 — El contador de intentos del menú cuenta todo el historial

- **Dónde:** `FabricaContextoRegla.cs:278-288`.
- **Evidencia:**
  - Si la conversación no tiene cuenta, los intentos son el total de entrantes menos uno.
  - Después de que la R9 (repregunta) o la R20 limpian el contexto, un hilo con historia ya supera
    el umbral: el siguiente texto libre va **directo a «Sin clasificar», sin mostrar el menú**.
- **Impacto:** un postulante recurrente pierde el autoservicio.
- **Referencia:** R19, A12. Clase ❌.

#### AL3 — La repregunta de la R9 le quita el analista a quien está en proceso

- **Dónde:** `R09RepreguntaEmpresa.cs:42-46` y `ConversacionService.cs:306-320`.
- **Evidencia:**
  - Solo se omite la repregunta si hay `Contratado` o `Descartado`. Con una postulación `EnProceso`
    **sí** se repregunta.
  - `LimpiarCuentaContexto` pone `AnalistaAtendiendoId = null` y `PendienteClasificar`: el hilo
    pasa a verlo todo el equipo.
  - A la inversa, un `Descartado` en **cualquier** cuenta impide repreguntar. Esa persona no
    recibe el menú para postular a otra empresa.
- **Impacto:** se rompe la continuidad del proceso vivo, y un descartado no puede reorientarse.
- **Referencia:** R9, A2, A13, P2. Clase ❌.

#### AL4 — La R14 pisa transferencias y omite el aviso de la R6

- **Dónde:** `R14Ausencias.cs:46-55`.
- **Evidencia:**
  - Con el titular ausente, **cada** mensaje entrante reasigna el hilo al respaldo, aunque lo
    atienda un tercero por transferencia o escalamiento.
  - A diferencia de la R01, no emite el aviso multi-cuenta.
- **Referencia:** R14, R8, R6, A10, P2. Clase ❌.

#### AL5 — El menú de empresas no escala y ofrece opciones muertas

- **Dónde:** `EjecutorAcciones.cs:296-307` y `CuentaService.cs:11-16`.
- **Evidencia:**
  - Se trunca a 10 opciones: con ~20 cuentas, la mitad es invisible.
  - Aparecen cuentas **sin titular**, contra lo que dice el README.
  - Aparecen vacantes **sin formulario**: el postulante elige y no recibe nada (`VacanteSinFormulario`).
  - No hay reconocimiento de código de aviso.
- **Referencia:** A6, R1, R20. Clase ❌ / 🔀.

#### AL6 — La anonimización (R17) deja datos personales

- **Dónde:** `PostulanteService.cs:76-117`.
- **Evidencia:** limpia `Postulante` y `JobFormsRespuestas`, pero no toca:
  - `Mensajes.Contenido` (el postulante suele escribir su DNI y su nombre);
  - `Conversaciones.TelefonoE164`;
  - `EventosSistema.Payload`, que guarda teléfono, texto y nombre en los avisos de transferencia;
  - `Auditoria.Detalle`.
- **Referencia:** R17, Ley 29733. Clase ⚠️.

#### AL7 — El kanban deduce el desenlace por nombre y no lo revierte

- **Dónde:** `PostulacionService.cs:79-84` y `AccionesBandeja.cs:47-50`.
- **Evidencia:**
  - Sacar la tarjeta de «Descartado» deja `Estado = Descartado`: una tarjeta en «Entrevista» que el
    sistema cree cerrada, y que la R16 archivará.
  - Volver a moverla a «Descartado» publica otro `PostulacionDescartada`: **segundo cierre de
    cortesía**.
  - Renombrar una etapa cambia el significado.
- **Referencia:** R12, R13, invariante 7, P4. Clase ❌.

#### AL8 — El límite de velocidad no obedece a su parámetro

- **Dónde:** `RegistroDependencias.cs:131` y `:145`.
- **Evidencia:**
  - El limitador toma `MaximoPorSegundo` de `appsettings`.
  - `envio.maximo_por_segundo`, sembrada y editable desde `/configuracion`, **no tiene efecto**.
  - La Api (respuestas de analistas) y el Worker (bot) tienen cada uno su limitador: el techo real
    es el doble.
- **Referencia:** §9.6.4, `CLAUDE.md` («velocidad de envío»). Clase 🔀.

#### AL9 — El aviso multi-cuenta se repite en cada mensaje

- **Dónde:** `R01Asignacion.cs:58-64`.
- **Evidencia:** mientras existan otras cuentas en proceso, **cada** entrante publica
  `AnalistaNotificado` al titular.
- **Referencia:** R6, P4. Clase ❌.

#### AL10 — Un postulante que vuelve después del archivado queda invisible

- **Dónde:** `ConversacionService.cs:378` (la bandeja excluye `Archivada`) y `R01Asignacion.cs:46-54`.
- **Evidencia:**
  - La conversación archivada conserva su cuenta y su analista.
  - Si la persona tiene un `Descartado`, la R9 no repregunta (AL3). La R1 no cambia el estado,
    porque solo actúa sobre `PendienteClasificar`.
  - El hilo **sigue `Archivada`**: nadie lo ve y la R2 no lo vigila.
- **Referencia:** A14, P3. Clase ❌.

### 5.3 Medios

**M1 — Eventos operativos sin consumidor**

- `EnvioRequierePlantilla`, `EnvioOmitidoSinPlantilla`, `VacanteSinFormulario`, `MenuSinOpciones`
  y `MenuTruncado` quedan `Pendiente` para siempre y nadie los ve.
- La consulta de la outbox filtra por `Tipo`, pero el índice (`Configuraciones.cs:373`) no lo
  incluye.
- No hay purga de procesados. SQL Express tiene un tope de 10 GB.

**M2 — Fallos de entrega invisibles**

- El acuse `failed` de Meta marca el mensaje, pero el chat no muestra `EstadoEntrega`
  (`PanelChat.razor:51-69`) y nadie avisa al analista.
- El analista cree que respondió y el postulante nunca lo recibió.

**M3 — Adjuntos del postulante perdidos**

- Imagen, documento y audio se guardan como `"[image]"`, sin descargar el medio
  (`InterpreteWebhookMeta.cs:121-122`).
- El postulante que manda su CV por WhatsApp, que es habitual, no deja nada utilizable.

**M4 — Escalamiento incompleto**

- Sin segundo nivel hacia Jefatura (A9).
- Sin plazo para «Sin clasificar» (P3).
- `EscalarAsync` recarga la fila y no revalida `FechaUltimaRespuestaAnalista`
  (`ConversacionService.cs:121-145`): hay una ventana para escalar lo que ya se respondió.

**M5 — Transferencias**

- Sin vencimiento ni retiro (A1 de la Fase 1), sin índice único filtrado de pendiente.
- Se permite transferir a un analista ausente; la pantalla no lo excluye.

**M6 — El archivado (R16) es parcial**

- `EstadoPostulacion.Archivada` nunca se asigna.
- Las postulaciones `EnProceso` inactivas nunca se archivan.
- No hay aviso previo ni reactivación explícita.

**M7 — Métricas sesgadas**

- La primera respuesta se mide **una vez por conversación en el período**
  (`ReportingReadModel.cs:52-76`), así que las esperas posteriores del mismo hilo no cuentan.
- Se mide en minutos de reloj pero se compara contra el plazo de la R2, que va en horas hábiles.

**M8 — Borde público del JobForms**

- **Límite de velocidad:** 30 por minuto por IP también sobre `webhook-google`
  (`JobFormsController.cs:23`). Las IPs de salida de Apps Script son compartidas; en una campaña
  masiva un 429 puede agotar los 3 reintentos del script y perder la postulación.
- **Datos sin validar:**
  - `CvUrl` acepta cualquier URL, y el analista termina haciendo clic en un enlace que llegó desde
    internet.
  - El DNI no se valida: ni formato ni nulo, que da un 500.
- **CV huérfano:** en el camino propio el CV se guarda antes de validar, y un rechazo lo deja sin
  fila (`:127-145`).

**M9 — Pruebas de integración insuficientes**

- EF InMemory no aplica índices únicos, RowVersion ni transacciones.
- No hay escenarios de varios mensajes sobre el mismo hilo, que es por donde se colaron C1, C2, AL2,
  AL3 y AL10.
- Las 4 pruebas de SQL Server se omiten por defecto.

**M10 — JWT sin revocación**

Un analista desactivado o con el rol cambiado conserva el token hasta 9 h.

**M11 — Valor por defecto peligroso**

`Plantilla.Activa = true` en la entidad (`Conversaciones.cs:171`) contradice V8 si alguien crea una
plantilla desde código.

### 5.4 Bajos

| # | Hallazgo | Dónde |
|---|---|---|
| B1 | Literales de tiempo o tope fuera de `ConfiguracionReglas`: 8 h (R3), `ReintentosPermitidos = 1` (R19), texto de horario por defecto | `R03…:40,47`; `R19…:18` |
| B2 | Auditoría de alta de vacante con `EntidadId = "0"`: se agrega antes de guardar | `CuentaService.cs:49` |
| B3 | `EstadoConversacion.Cerrada` sin uso: no hay «cerrar conversación» | `Enumeraciones.cs:42` |
| B4 | El hash falso de login cuesta dos PBKDF2: la señal de tiempo queda invertida | `AutenticacionService.cs:47,119` |
| B5 | La inactividad de la R9 se mide solo entre entrantes (A13 pide cualquier dirección) | `FabricaContextoRegla.cs:207-221` |
| B6 | En «Sin clasificar», cada mensaje vuelve a cambiar el estado y a auditar | `R19FallbackMenu.cs:42-46` |
| B7 | `DateTime.UtcNow` directo en servicios y casos (sin `TimeProvider`): el tiempo no se puede probar de punta a punta | varios |
| B8 | 89 archivos sin commitear: riesgo de pérdida y de mezclar la auditoría con cambios en curso | árbol de trabajo |
| B9 | `JobFormsRespuestas` sin índice único por `InvitacionId` | `Configuraciones.cs:350` |

---

## 6. Seguridad

| Control | Estado | Nota |
|---|---|---|
| Autorización por defecto; lo público se marca a mano | ✅ | `Program.cs:80-88` |
| Autorización por objeto (conversación, cuenta, vacante, ausencia) | ✅ | Salvo «Sin clasificar» (AL1) |
| Hash de contraseñas y login uniforme | ✅ | PBKDF2 con 210.000 iteraciones (B4 menor) |
| Firma del webhook | ✅ | HMAC en tiempo fijo; sin secreto se rechaza todo |
| Secreto del Apps Script | ✅ | Tiempo fijo |
| Tokens no enumerables | ✅ | GUID |
| Rate limiting | ⚠️ | Correcto en login; riesgoso en `webhook-google` (M8) |
| Validación de CV | ✅ / ⚠️ | Extensión, tamaño, cuarentena y Defender ✅; `CvUrl` sin validar (M8) |
| Secretos y arranque | ✅ | La Api no arranca sin clave; arranque acotado (V25) |
| Revocación de sesión | ⚠️ | M10 |
| Datos personales en outbox y logs | ⚠️ | AL6, M1 |

---

## 7. Calidad de código y pruebas

**Fortalezas**

- Nombres y convenciones consistentes.
- Comentarios que explican el porqué, atados a la regla o la sección del dossier.
- Manejo explícito de carreras (`ObtenerOCrear`, índice único).
- Clasificación de fallos de envío.
- 349 pruebas rápidas.

**Deudas**

- **Pruebas por regla aisladas del flujo.** El contexto se arma a mano y no refleja lo que la
  fábrica y el webhook producen de verdad (C1). Hace falta una batería de *escenarios de
  conversación* sobre el `EntornoDeReglas` existente, con varios mensajes, tiempo simulado y
  plantillas inactivas.
- **Sin transacción por unidad de trabajo** en los casos de uso: raíz de C5 y C6.
- **Desenlaces por cadena de texto** (AL7) y constantes de negocio en el código (B1).
- **Sin `TimeProvider`** (B7), que es lo que impide probar los barridos de punta a punta.

---

## 8. README contra código (🔀)

| Afirmación del README | Realidad |
|---|---|
| «Una cuenta sin titular, o sin vacantes abiertas, no aparece en el menú del bot» | Sin titular **sí** aparece (AL5) |
| El adaptador limita el saliente con `envio.maximo_por_segundo` (`CLAUDE.md`) | Se lee de `appsettings`, por proceso, y los reintentos de Polly lo saltan (AL8, C4) |
| «Se reintenta el mensaje, no el evento de la outbox» | El consumidor reprocesa el evento completo ante cualquier excepción (C5) |
| «Reglas implementadas: … 3 …» | R3 no dispara en el flujo real (C1) |
| «Reglas: 1, 2, 3 … en Domain» (estructura) | Las reglas están en Application |

---

## 9. Lo que se conserva tal cual

- La estructura de proyectos y sus límites.
- El motor de reglas: Strategy, `AccionRegla` y el ejecutor único.
- Autenticación y autorización: la política por defecto, `FiltroAccesoConversacion` y las
  políticas por rol.
- El adaptador multi-proveedor con intérprete compartido.
- Idempotencia del webhook por `ProviderMessageId`.
- El candado de instancia del Worker, los latidos y el health de dos niveles.
- `ReintentoEnvios`: la clasificación y la revalidación de la R15. Solo hace falta quitarle la
  competencia de Polly (C4).
- El circuito del JobForms: token, `{token}` en la URL, idempotencia de V27, escaneo y cuarentena.
- Los scripts de despliegue, respaldo y verificación.
- `docs/decisiones.md` como registro vivo.

---

## 10. Insumos para la Fase 3

La Fase 3 separará estos hallazgos en tres grupos:

1. **Arquitectura y diseño faltante:**
   - unidad de trabajo transaccional y outbox de envíos idempotente (C5, C6);
   - política HTTP sin reintentos automáticos de POST (C4);
   - campos y estados nuevos: `Reingreso`, `CodigoAviso`, `FechaAvisoFueraHorario`,
     `EstadoResultante`, estado del menú, vencimiento de transferencias;
   - índices y purga de la outbox;
   - medios entrantes.
2. **Funciones faltantes:**
   - tomar y clasificar hilos;
   - segundo nivel de escalamiento y plazo de «Sin clasificar»;
   - vencimiento y retiro de transferencias;
   - aviso previo y reactivación del archivado;
   - prefijo y desambiguación multi-cuenta;
   - opción «no enviar cierre»;
   - estado de entrega en el chat;
   - panel de eventos operativos;
   - revocación de sesión.
3. **Correcciones:** C1, C2, C3 y AL1–AL10, M4, M6–M8, M11, B1–B9. Cada una con su prueba de
   escenario.

**Prerrequisito operativo antes de tocar código:** commitear o respaldar los 89 archivos modificados
(B8).
