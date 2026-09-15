# Documento de continuidad de desarrollo — RRHH WhatsApp

> **Para quién es.** Para la IA, o la persona, que retome el desarrollo después de la auditoría del
> 2026-09-14. Se lee de arriba abajo **una vez**. Después se trabaja tarea por tarea desde la §5.
>
> **Qué contiene**
>
> | Sección | Para qué |
> |---|---|
> | §1 Resumen del estado del sistema | Qué hay y qué falla |
> | §2 Protocolo de trabajo | Cómo tomar, implementar, verificar y cerrar una tarea |
> | §3 Reglas que no se rompen | Los límites que ninguna tarea puede cruzar |
> | §4 Mapa del código | Dónde vive cada cosa |
> | §5 Backlog priorizado | Tareas exactas en orden de dependencias, con archivos |
> | §6 Registro de avance | Se actualiza al cerrar cada tarea |
>
> **Documentos de la auditoría** (en `docs/auditoria/`)
>
> | Documento | Qué aporta | Cuándo leerlo |
> |---|---|---|
> | `01-arquitectura-funcional.md` | Qué debe hacer el sistema. **§5 = resoluciones A1–A15 y principios P1–P4, vigentes** | Al empezar |
> | `02-auditoria-codigo.md` | Hallazgos C#, AL#, M#, B# con evidencia | Cuando una tarea cite un hallazgo |
> | `03-analisis-brechas.md` | **Especificación detallada** de cada brecha (ARQ, FUN, COR): campos, firmas, endpoints | **Antes de cada tarea**, en la sección de su brecha |
> | este documento | El orden y el paso a paso | Siempre |

---

## 1. Resumen del estado del sistema

### 1.1 Qué es

- **Negocio:** sistema interno de RRHH que reemplaza WhatsApp Business multi-PC por la WhatsApp
  Business API. 13 analistas, ~20 cuentas, ~16.000 interacciones al mes.
- **Stack:** .NET 10, ASP.NET Core, EF Core 10, SQL Server, Blazor Server, 360dialog / Meta Cloud
  API, IIS on-premise.
- **Tres procesos:**
  - **Api:** webhook, REST y hub SignalR.
  - **Worker:** outbox, barridos por tiempo, purga y reintentos.
  - **Frontend:** bandeja Blazor.
- **Criterio rector validado con el usuario:** **atención preferente al postulante** (Fase 1 §5).

### 1.2 Punto de partida

- **Repositorio:** rama `master`, commit `862ac92`, con **89 archivos modificados sin commitear**.
  La tarea T0.01 los consolida.
- **Build:** 0 errores, 0 advertencias.
- **Pruebas:** 349 superadas, 0 fallidas, 4 omitidas (necesitan `RRHH_PRUEBAS_SQL`).

### 1.3 Estado por componente (real, no el que declara el README)

| Componente | Estado | Nota |
|---|---|---|
| Límites entre proyectos | ✅ Listo | Sin violaciones |
| Autenticación JWT y autorización por rol y objeto | ✅ Listo | Falta revocación (T5.09) |
| Webhook: firma e idempotencia por `ProviderMessageId` | ✅ / ❌ | La ingesta no es atómica (T1.03) |
| Motor de reglas (Strategy) | ✅ Listo | Hay que ampliar contexto y acciones (T2.11) |
| Envío saliente | ❌ Riesgo de duplicados | La resiliencia HTTP reintenta (T0.03); el evento se reprocesa (T1.05–T1.12) |
| Outbox y Worker, candado, latidos, health | ✅ / ⚠️ | Consumidor no transaccional (T1.12) |
| R1 Asignación, R4 Visibilidad, R7 Marcas, R10, R15 | ✅ / ⚠️ | Ajustes menores |
| R2 Escalamiento | ⚠️ | Sin segundo nivel (T4.01) |
| **R3 Fuera de horario** | ❌ | Nunca dispara (T3.02) |
| **R9 JobForms** | ❌ | Menú repetido (T3.04); confirmación muda (T3.01); repregunta quita el analista (T3.09) |
| R6/R11 Multi-cuenta | ⚠️ | Aviso repetido (T3.11); sin desambiguación (T4.07) |
| R8 Transferencias | ⚠️ | Sin vencimiento (T4.04) |
| R12 Cierre, R13 Kanban | ⚠️ | Cierre duplicado y desenlace por nombre (T3.12, T4.09) |
| R14 Ausencias | ⚠️ | Pisa transferencias (T3.10) |
| R16 Archivado | ⚠️ / ❌ | Parcial; el hilo reactivado queda invisible (T4.11–T4.12) |
| R17 Datos personales | ⚠️ | Anonimización incompleta (T5.07) |
| R18 Métricas | ⚠️ | Sesgo por hilo y minutos de reloj (T5.13) |
| R19 Menú no reconocido | ❌ | Contador roto; «Sin clasificar» sin dueño (T3.05, T3.13) |
| R20 Vacante cerrada | ✅ / ⚠️ | El aviso no sale sin plantilla (T3.01) |
| JobForms (Google Forms + Apps Script) | ✅ / ⚠️ | Límite por IP y 429 sin reintento (T0.06) |
| Adjuntos entrantes | ❌ | No se descargan (T5.02–T5.05) |
| Administración de analistas, cuentas y vacantes | ⚠️ | Sin baja ni edición (T5.11, T5.12) |
| Bandeja Blazor | ✅ / ⚠️ | Falta tomar, «vencida», estado de entrega, adjuntos |
| Scripts de despliegue y respaldo | ✅ Listo | — |

### 1.4 Datos que dependen de personas

**Una tarea que los necesite se detiene y pregunta.**

| Dato | Lo usa | Quién lo da |
|---|---|---|
| `WhatsApp:NumeroPublico` (E.164 del WABA) | T3.08 | Sistemas / RRHH |
| Confirmación legal de `datos.retencion_cv_dias` y la versión del aviso | T5.07 (no bloquea: se usan los valores sembrados) | Legal |
| Aprobación de plantillas en Meta | Nunca se activan desde código | Meta / Sistemas |
| Dominios válidos de CV (`JobForms:DominiosCvPermitidos`) | T0.07 (por defecto `drive.google.com`, `docs.google.com`) | Sistemas |
| Ratificación de las decisiones V28–V36 | T0.02 | Usuario, a través del agente `arquitecto` |

---

## 2. Protocolo de trabajo (obligatorio)

### 2.1 Al iniciar una sesión

1. Leer `CLAUDE.md`, `README.md`, `docs/decisiones.md` y este documento: §1, §2, §3 y §6.
2. Correr `dotnet build --nologo` y `dotnet test --nologo`. Si algo está rojo **antes** de tocar
   nada, se arregla primero o se reporta.
3. En §6, buscar la **primera tarea `[ ]` cuyas dependencias estén todas `[x]`**. Es la tarea de la
   sesión.

### 2.2 Para cada tarea

1. **Leer la especificación** de su brecha en `03-analisis-brechas.md`. La tarea dice cuál. Si la
   tarea y la especificación difieren, manda la especificación, y la diferencia se anota en §6.
2. **Elegir el agente dueño** que indica la tarea (tabla de agentes de `CLAUDE.md`). Si la tarea
   cruza dos superficies, se consulta primero a `arquitecto`.
3. **Escribir primero la prueba** que demuestra la brecha (unitaria o escenario E##) y verificar
   que **falla**.
4. **Implementar en el orden de capas:**
   1. `Domain`: entidades, enums, interfaces, `ContextoRegla`, `AccionRegla`.
   2. `Infrastructure/Persistencia`: `Configuraciones.cs`, `RrhhDbContext`, `DatosSemilla`.
   3. **Migración nueva** si cambió el esquema (§2.3).
   4. `Infrastructure/Servicios`: servicio de dominio. **No hay capa Repository (V6):** el servicio
      es la frontera de datos.
   5. `Application`: regla, caso de uso, ejecutor.
   6. `Contracts`: DTOs.
   7. `Api`: controlador, filtro, política.
   8. `Worker`: bucle, latido.
   9. `Frontend`: `ClienteApi`, componente.
5. **Registrar la dependencia** nueva en `RegistroDependencias.cs` (servicios, reglas) o en
   `Program.cs` (Api, Worker).
6. **Verificar:** `dotnet build --nologo` y `dotnet test --nologo` en verde. Si la tarea toca
   persistencia real (transacciones, índices, migraciones) y existe `RRHH_PRUEBAS_SQL`, también
   `dotnet test --filter "FullyQualifiedName~SqlServer"`.
7. **Cerrar la tarea:**
   - marcar `[x]` en §6 con fecha y notas;
   - si cambió un comportamiento descrito en el README, actualizarlo en la misma tarea;
   - **commit solo si el usuario lo autorizó en la sesión**, con el mensaje `T#.##: título corto`.

### 2.3 Migraciones

```bash
dotnet ef migrations add NombreDelCambio --project src/RRHH.WhatsApp.Infrastructure --output-dir Persistencia/Migraciones
```

- Usar **exactamente** el nombre de migración que dice la tarea.
- **Nunca editar** una migración ya aplicada: las 6 existentes (`Inicial` … `ReintentoDeEnvios`)
  están cerradas.
- Revisar el `.cs` generado antes de dar la tarea por hecha. **Motivo:** V17 documenta una
  migración que borraba un índice por error.
- Los índices con filtro llevan nombre explícito: `HasIndex(x => ..., "IX_...")`.

### 2.4 Cuándo detenerse y preguntar

- Una tarea necesita un dato de la §1.4.
- La especificación exige cruzar un límite entre proyectos no previsto.
- Una prueba existente contradice la especificación. **Nunca se borra una prueba para ponerla en
  verde:** se explica el conflicto.
- Aparece una brecha no listada. En ese caso se agrega una tarea con sufijo (por ejemplo `T3.04a`)
  en §6 y se sigue con la actual si no bloquea.

---

## 3. Reglas que no se rompen

1. **Límites entre proyectos** (`CLAUDE.md`). El Frontend solo referencia Contracts; Reporting solo
   Domain y Contracts.
2. **Las reglas deciden y no ejecutan.** `IReglaNegocio.EvaluarAsync` devuelve `ResultadoRegla`, sin
   base de datos ni proveedor. Todo efecto va en `EjecutorAcciones` o en un servicio.
3. **R15 en todo saliente:** sin `FechaOptIn` no sale nada; fuera de las 24 h desde el último
   **entrante**, solo una plantilla activa.
4. **Las plantillas nunca se activan** desde código ni desde migraciones. `Activa = false` es el
   valor por defecto.
5. **Nada evita el limitador de envío.** Tras T0.03 **no hay reintentos HTTP implícitos**: el único
   reintento es `ReintentoEnvios`.
6. **Nada de literales de tiempo o topes de negocio.** Todo parámetro nuevo va en
   `ClavesConfiguracion` más la semilla de `ConfiguracionReglas`. Excepción: los límites de Meta
   (ventana de 24 h, 3 botones, 10 filas, largos de título), como constantes comentadas.
7. **UTC en base de datos**, `America/Lima` en pantalla. Tras T0.08, el tiempo se lee de
   `TimeProvider`, nunca de `DateTime.UtcNow`, en Application e Infrastructure.
8. **`Conversacion` es el hilo** (uno por teléfono) y **`Postulacion` es el proceso** (persona más
   vacante). No se mezclan (V1).
9. **El analista sale del token**, nunca del cuerpo ni de la query. Quien no ve recibe 404; quien
   ve pero no actúa, 403.
10. **Identificadores en español sin tildes.** Los comentarios explican el porqué y citan la regla,
    la resolución (A#) o la decisión (V#).
11. **Principios P1–P4:**
    - P1: el bot habla en texto libre dentro de la ventana;
    - P2: continuidad del analista;
    - P3: todo hilo con dueño o con plazo;
    - P4: cero repeticiones al postulante.

---

## 4. Mapa del código

| Qué | Dónde |
|---|---|
| Entidades | `src/RRHH.WhatsApp.Domain/Entidades/` — `Organizacion.cs` (Cuenta, Analista, AnalistaCuenta, Ausencia, HorarioAtencion, Hc, HcCampoOpcional), `Conversaciones.cs` (Conversacion, Mensaje, Transferencia, Plantilla), `Postulantes.cs` (Postulante, Postulacion, EstadoPostulanteCuenta, EtapaKanban), `JobForms.cs`, `Sistema.cs` (EventoSistema, ConfiguracionRegla, **ClavesConfiguracion**, Auditoria), `LatidoServicio.cs`, `IdsBoton.cs`, `ClavesPlantilla.cs`, `EnlaceJobForms.cs` |
| Enums | `Domain/Enums/Enumeraciones.cs` |
| Interfaces de servicios | `Domain/Interfaces/ServiciosDominio.cs`, `IWhatsAppProvider.cs`, `DatosJobForms.cs` |
| Motor: contratos | `Domain/Reglas/ContextoRegla.cs`, `AccionRegla.cs`, `IReglaNegocio.cs` |
| Reglas | `Application/Reglas/Implementaciones/R##*.cs`; motor en `Application/Reglas/MotorReglas.cs`; fábrica (interfaz) en `IFabricaContextoRegla.cs` |
| Casos de uso | `Application/Casos/`: `RecepcionWebhook` (y `TiposEvento`), `ProcesadorOutbox`, `EvaluadorReglas`, `EjecutorAcciones`, `EnvioAnalista`, `AccionesBandeja`, `RecepcionJobForms`, `BarridoTiempo`, `ReintentoEnvios` |
| Persistencia | `Infrastructure/Persistencia/`: `RrhhDbContext.cs`, `Configuraciones.cs`, `DatosSemilla.cs`, `Migraciones/` |
| Servicios | `Infrastructure/Servicios/*Service.cs`, `FabricaContextoRegla.cs`, `ZonaHorariaPeru.cs` |
| Proveedores | `Infrastructure/Proveedores/`: `MetaCloudProvider`, `Dialog360Provider`, `ProveedorSimulado`, `CuerposMensaje`, `InterpreteWebhookMeta`, `LimitadorEnvio` |
| Composición | `Infrastructure/RegistroDependencias.cs` (servicios, **reglas en orden**, proveedor) |
| Api | `Api/Program.cs`, `Api/Controllers/*`, `Api/Seguridad/*` (`FiltroAccesoConversacion`, `Politicas`, `EmisorTokens`), `Api/TiempoReal/*`, `Api/Salud/ChequeoWorker.cs`, `Api/Mapeo/MapeoBandeja.cs` |
| Worker | `Worker/Program.cs`, `ConsumidorOutbox`, `ServicioBarridoTiempo`, `ServicioPurgaCv`, `ServicioReintentoEnvios`, `GuardiaInstancia`, `Latido.cs`, `OpcionesWorker.cs` |
| Reporting | `Reporting/ReportingReadModel.cs`, `Persistencia/ReportingDbContext.cs` |
| Contracts | `Contracts/Bandeja/ContratosBandeja.cs`, `PeticionesBandeja.cs`, `Administracion/`, `Metricas/`, `Seguridad/`, `TiempoReal/` |
| Frontend | `Frontend/Servicios/ClienteApi.cs`, `SesionAnalista.cs`, `CanalEnVivo.cs`, `HoraLima.cs`; `Components/Pages/*` (Bandeja, Tablero, Vacantes, Equipo, Configuracion, Metricas, MiCuenta); `Components/Bandeja/*` (ListaConversaciones, PanelChat, CajaRespuesta, AccionesRapidas, TransferenciasRecibidas) |
| Pruebas | `tests/RRHH.WhatsApp.Tests/`: `Reglas/` (unitarias con `ConstructorContexto`), `Casos/` (con `EntornoDeReglas`, el circuito completo en memoria), `Infraestructura/`, `Proveedores/`, `Reporting/`; **nuevo:** `Escenarios/` |
| Apps Script | `scripts/apps-script/Codigo.gs` |

**Orden de prioridad de las reglas (actual).** Una regla nueva se ubica según su prioridad:

| Prioridad | Regla |
|---|---|
| 10 | R15 |
| 15 | R19 |
| 18 | R09 repregunta |
| 20 | R14 |
| 22 | R16 |
| 25 | R02 |
| 30 | R01 |
| 40 | R09 seguimiento |
| 42 | R20 |
| 45 | R09 enlace |
| 46 | R09 confirmación |
| 50 | R03 |
| 60 | R12 |

**Glosario**

| Término | Significado |
|---|---|
| HC | Vacante |
| Titular / respaldo | Analista principal y de reserva de una cuenta |
| Ventana | 24 h desde el último mensaje entrante |
| Opt-in | `Conversacion.FechaOptIn` |
| Tanda | Mensajes entrantes consecutivos desde la última respuesta humana |
| Sin clasificar | Estado `PendienteClasificar` |
| En menú del bot | Estado `EnMenuBot` (T2.03) |

---

## 5. Backlog priorizado

**Formato de cada tarea**

- **Brecha:** id en `03` · **Agente** · **Depende de**
- **Crear** / **Modificar:** rutas desde `src/` o `tests/` (se omite el prefijo `RRHH.WhatsApp.`
  cuando es obvio)
- **Pasos:** en orden
- **Hecho cuando:** verificable

---

### BLOQUE 0 — Preparación y estabilización

> **Objetivo:** congelar la línea base y cortar ya los riesgos de bloqueo con Meta. Son tareas chicas
> e independientes del rediseño.

#### T0.01 · Línea base del repositorio

- **Brecha:** COR-20 · **Agente:** `despliegue-operacion` · **Depende de:** —
- **Pasos**
  1. `git status`: confirmar los 89 archivos modificados y los nuevos de `docs/auditoria/`.
  2. **Pedir autorización al usuario** para crear la rama `auditoria/linea-base` y commitear.
  3. Correr build y pruebas en verde, y hacer el commit
     `Linea base previa a la continuidad (auditoria 2026-09-14)`.
- **Hecho cuando:** `git status` queda limpio en la rama y las pruebas pasan.

#### T0.02 · Registrar las decisiones nuevas y apuntar a este documento

- **Brecha:** COR-19 (§5 de `03`) · **Agente:** `arquitecto` · **Depende de:** T0.01
- **Modificar:** `docs/decisiones.md`, `CLAUDE.md`
- **Pasos**
  1. Presentar V28–V36 al usuario (tabla §5 de `03`) y obtener ratificación. Si rechaza alguna,
     anotar la alternativa acordada en §6 y ajustar las tareas afectadas.
  2. En `decisiones.md`:
     - fila **D7**: «Resoluciones A1–A15 con criterio de atención preferente al postulante» con
       enlace a `01 §5`;
     - una sección por cada V28–V36 con el formato existente (Problema / Decisión / Consecuencia).
  3. En `CLAUDE.md`, bajo «Lee siempre antes de trabajar», agregar:
     `docs/auditoria/04-documento-continuidad.md — backlog vigente y protocolo de trabajo`.
- **Hecho cuando:** las decisiones están registradas y `CLAUDE.md` apunta a este documento.

#### T0.03 · Proveedores sin reintentos HTTP implícitos

- **Brecha:** ARQ-04 (hallazgo C4) · **Agente:** `integracion-whatsapp` · **Depende de:** T0.01
- **Modificar**
  - `Infrastructure/RegistroDependencias.cs`: quitar las dos llamadas `.AddStandardResilienceHandler()`
    (líneas ~140 y ~164).
  - `Infrastructure/Proveedores/MetaCloudProvider.cs` y `Dialog360Provider.cs`
    (`EnviarAsync`): agregar un `catch (Exception ex) when (!ct.IsCancellationRequested)` que
    devuelva `ResultadoEnvio.Ambiguo(...)`. El `HttpRequestException` sin respuesta queda
    `Transitorio`, como hoy.
  - `Infrastructure/RRHH.WhatsApp.Infrastructure.csproj`: quitar `Microsoft.Extensions.Http.Resilience`
    si queda sin uso.
- **Crear:** en `tests/.../Proveedores/MetaCloudProviderTests.cs`, pruebas con un
  `HttpMessageHandler` falso:
  - 503 da una sola petición y `Transitorio`;
  - excepción tras enviar da `Ambiguo` sin propagar.
- **Hecho cuando:** las pruebas nuevas pasan y ninguna excepción sale de `EnviarAsync`.

#### T0.04 · El limitador obedece a `envio.maximo_por_segundo`

- **Brecha:** COR-12 (AL8) · **Agente:** `integracion-whatsapp` · **Depende de:** T0.01
- **Crear:** `Infrastructure/Proveedores/ProveedorParametrosEnvio.cs`, singleton:
  - usa `IServiceScopeFactory` para leer `IConfiguracionReglasService.ObtenerTodasAsync`;
  - caché de 30 s;
  - fallback al valor de appsettings.
- **Modificar**
  - `LimitadorEnvio.cs`: el constructor recibe `Func<int> maximoActual` y lo evalúa en cada
    `EsperarTurnoAsync`. Si el valor no es positivo, usa 1.
  - `RegistroDependencias.cs`: construir el limitador con el proveedor de parámetros.
  - `OpcionesMetaCloud.cs`, `Dialog360Opciones.cs`: documentar `MaximoPorSegundo` como fallback.
- **Hecho cuando:** una prueba cambia el parámetro y el limitador respeta el nuevo tope tras
  expirar la caché.

#### T0.05 · Plantillas inactivas por defecto

- **Brecha:** COR-16 (M11) · **Agente:** `integracion-whatsapp` · **Depende de:** T0.01
- **Modificar:** `Domain/Entidades/Conversaciones.cs`: `Plantilla.Activa { get; set; } = false;`
  con un comentario que cite V8.
- **Crear:** prueba `new Plantilla{...}.Activa == false`.
- **Hecho cuando:** pasa la prueba y no hay cambios en el snapshot de EF, porque la semilla ya era
  `false`.

#### T0.06 · Límite de velocidad propio del webhook de Google y reintento del 429

- **Brecha:** COR-15 (M8) · **Agente:** `jobforms-datos` · **Depende de:** T0.01
- **Modificar**
  - `Api/Configuracion/PoliticasLimite.cs`: constante `WebhookGoogle`.
  - `Api/Configuracion/OpcionesJobForms.cs`: `LimitePorMinutoWebhook` (600).
  - `Api/Program.cs`: política particionada por la clave fija `"apps-script"`, con ese límite.
  - `Api/Controllers/JobFormsController.cs`: en `WebhookGoogle`, `[EnableRateLimiting(PoliticasLimite.WebhookGoogle)]`,
    que reemplaza la de la clase para esa acción.
  - `Api/appsettings.json`: `JobForms:LimitePorMinutoWebhook`.
  - `scripts/apps-script/Codigo.gs` (`entregar`): tratar `429` como reintentable, leyendo
    `Retry-After` si existe, y `REINTENTOS = 5`.
- **Hecho cuando:** la prueba de contrato del Apps Script (`ContratoAppsScriptTests.cs`) cubre que
  el 429 no se trata como rechazo, y 100 envíos por minuto desde una misma IP no reciben 429.

#### T0.07 · Validación del cuerpo público del JobForms

- **Brecha:** COR-15 (M8) · **Agente:** `jobforms-datos` · **Depende de:** T0.06
- **Crear:** `Domain/Entidades/DocumentoIdentidad.cs`, con
  `static bool TryNormalizar(string? valor, out string normalizado)`: 8 dígitos (DNI) o 9 a 12
  alfanuméricos (carné de extranjería), en mayúsculas y sin espacios.
- **Modificar**
  - `OpcionesJobForms.cs`: `string[] DominiosCvPermitidos = ["drive.google.com", "docs.google.com"]`.
  - `JobFormsController.cs`:
    - DNI inválido o nulo: 422;
    - `CvUrl` no `https` o fuera de los dominios permitidos: 422;
    - en `Enviar`, validar invitación vigente y vacante abierta **antes** de `AlmacenarCvAsync`, y
      borrar el CV guardado si `recepcion.ProcesarAsync` lanza.
  - `Domain/Interfaces/ServiciosDominio.cs` e `Infrastructure/Servicios/JobFormsService.cs`:
    `Task EliminarCvAsync(string ruta, CancellationToken ct)`.
- **Hecho cuando:** hay pruebas para DNI nulo (422), un dominio malicioso (422) y un CV sin fila
  tras un rechazo (el archivo no existe).

#### T0.08 · `TimeProvider` en Infrastructure

- **Brecha:** ARQ-01 · **Agente:** `backend-datos` · **Depende de:** T0.01
- **Modificar**
  - `Infrastructure/RegistroDependencias.cs`: `services.AddSingleton(TimeProvider.System)`.
  - Todos los `Infrastructure/Servicios/*Service.cs` y `FabricaContextoRegla.cs`: inyectar
    `TimeProvider reloj` y reemplazar `DateTime.UtcNow` por `reloj.GetUtcNow().UtcDateTime`.
- **Hecho cuando:** `grep "DateTime.UtcNow" src/RRHH.WhatsApp.Infrastructure` no da resultados
  (salvo `ZonaHorariaPeru`, si aplica) y las pruebas pasan, actualizando los constructores en
  `tests`.

#### T0.09 · `TimeProvider` en Application y Api

- **Brecha:** ARQ-01 · **Agente:** `backend-datos` · **Depende de:** T0.08
- **Modificar**
  - `Application/Casos/*.cs`: `EnvioAnalista`, `ReintentoEnvios`, `RecepcionJobForms`, más
    cualquier otro que use el reloj.
  - `Api/Mapeo/MapeoBandeja.cs`: `AResumen(this Conversacion c, DateTime ahoraUtc, ...)` y ajustar
    los llamadores en los controladores (inyectar `TimeProvider`).
- **Hecho cuando:** no queda `DateTime.UtcNow` en Application ni en `Api/Mapeo`, y build y pruebas
  están en verde.

#### T0.10 · Reloj simulado en el entorno de pruebas

- **Brecha:** ARQ-01, ARQ-12 · **Agente:** `backend-datos` · **Depende de:** T0.09
- **Modificar**
  - `tests/.../RRHH.WhatsApp.Tests.csproj`: paquete `Microsoft.Extensions.TimeProvider.Testing`.
  - `tests/.../Casos/EntornoDeReglas.cs`:
    - propiedad `FakeTimeProvider Reloj`, que empieza en un lunes a las 10:00 hora de Lima, en UTC;
    - método `Task AvanzarAsync(TimeSpan lapso)`;
    - pasar `Reloj` a todos los servicios;
    - `IngresarAsync` usa `Reloj` en vez de `DateTime.UtcNow`.
- **Hecho cuando:** las pruebas existentes pasan con el reloj simulado.

---

### BLOQUE 1 — Confiabilidad: nada se duplica ni se pierde

#### T1.01 · Unidad de trabajo transaccional

- **Brecha:** ARQ-02 · **Agente:** `backend-datos` (V28) · **Depende de:** T0.10, T0.02
- **Crear**
  - `Infrastructure/Persistencia/UnidadTrabajoEf.cs`: implementa `IUnidadTrabajo`. Reutiliza la
    transacción abierta si existe; si no, `BeginTransactionAsync(ReadCommitted)`, luego `trabajo`,
    luego `CommitAsync`. En el `catch`: `RollbackAsync`, `ChangeTracker.Clear()` y relanzar.
- **Modificar**
  - `Domain/Interfaces/ServiciosDominio.cs`: interfaz `IUnidadTrabajo` (firma en `03` §ARQ-02).
  - `Infrastructure/RegistroDependencias.cs`: `AddScoped<IUnidadTrabajo, UnidadTrabajoEf>()`.
  - `tests/.../Casos/EntornoDeReglas.cs` y demás usos de `UseInMemoryDatabase`:
    `.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))`.
- **Hecho cuando:** la prueba unitaria que ejecuta un trabajo que lanza propaga la excepción y
  limpia el ChangeTracker.

#### T1.02 · Base de pruebas contra SQL Server y arnés de escenarios

- **Brecha:** ARQ-12 · **Agente:** `backend-datos` · **Depende de:** T1.01
- **Crear**
  - `tests/.../Infraestructura/SqlServerFixture.cs`: si `RRHH_PRUEBAS_SQL` existe, crea la base
    `RRHH_Pruebas_{guid:N}`, aplica `MigrateAsync()` y la borra al terminar. Si no existe, las
    pruebas se omiten con `Skip`.
  - `tests/.../Escenarios/PasoConversacion.cs`: records `Entrante(string Texto)`,
    `Boton(string IdBoton)`, `Avanzar(TimeSpan)`, `Formulario(...)`, `RespuestaAnalista(...)`,
    `Barrido()`, `ConsumirOutbox()` y `Despachar()`. Este último queda como no-op hasta T1.09.
  - `tests/.../Escenarios/ArnesEscenario.cs`: envuelve `EntornoDeReglas`, con
    `ConversarAsync(params PasoConversacion[])` y los accesos `Enviados()`, `EstadoConversacion()`,
    `Notificaciones()` y `Alertas()`.
- **Modificar:** `tests/.../Casos/EntornoDeReglas.cs`, para exponer lo que el arnés necesita.
- **Hecho cuando:** un escenario de humo («Hola» muestra el menú) pasa usando el arnés.

#### T1.03 · Webhook atómico

- **Brecha:** COR-05 (C6) · **Agente:** `integracion-whatsapp` · **Depende de:** T1.02
- **Modificar:** `Application/Casos/RecepcionWebhook.cs`. Inyectar `IUnidadTrabajo`; por cada `dto`:
  1. `ObtenerOCrearAsync` fuera de la transacción.
  2. `unidad.EjecutarAsync(ct => { RegistrarEntrante; si es nulo, duplicado y return; RegistrarEntrada; Publicar })`.
  - Los acuses se procesan cada uno en su propia transacción.
- **Crear:** `tests/.../Escenarios/E17IngestaAtomicaSqlTests.cs`, con SQL. Un
  `IEventoSistemaService` decorado que lanza en `PublicarAsync` deja 0 filas en `Mensajes`, y la
  reentrega del mismo `wamid` produce `MensajesNuevos = 1`.
- **Hecho cuando:** E17 pasa con SQL y las pruebas en memoria siguen verdes.

#### T1.04 · Recepción del formulario atómica

- **Brecha:** COR-05 (C6) · **Agente:** `jobforms-datos` · **Depende de:** T1.01
- **Modificar:** `Application/Casos/RecepcionJobForms.cs`: todo el bloque desde
  `RegistrarDesdeFormularioAsync` hasta `PublicarAsync` dentro de `unidad.EjecutarAsync`. El chequeo
  de `yaRecibido` queda antes.
- **Crear:** prueba SQL: si falla la publicación, la invitación queda sin `Completado` y el
  reintento se procesa completo.
- **Hecho cuando:** la prueba pasa.

#### T1.05 · Cola de envíos: modelo de dominio

- **Brecha:** ARQ-03 · **Agente:** `integracion-whatsapp` (V29) · **Depende de:** T1.01
- **Modificar**
  - `Domain/Enums/Enumeraciones.cs`:
    - `enum TipoSaliente { Texto = 1, Plantilla = 2, Botones = 3, Lista = 4 }`;
    - `EstadoEntrega.EnCola = 6` y `Enviando = 7`.
  - `Domain/Entidades/Conversaciones.cs` (`Mensaje`): `TipoSaliente TipoSaliente = Texto`,
    `string? OpcionesJson`, `string? ClaveIdempotencia`.
  - `Domain/Interfaces/ServiciosDominio.cs`:
    - record `SalienteEncolado` (firma en `03` §ARQ-03);
    - en `IMensajeService`: `EncolarSalienteAsync`, `TomarLoteEnColaAsync(int maximo, CancellationToken ct)`
      y `RecuperarEnviandoVencidosAsync(TimeSpan antiguedad, CancellationToken ct)`.
- **Hecho cuando:** compila. Las implementaciones pueden quedar con `NotImplementedException`
  **solo** hasta T1.07, dentro del mismo bloque.

#### T1.06 · Cola de envíos: persistencia y migración

- **Brecha:** ARQ-03, ARQ-06 · **Agente:** `backend-datos` · **Depende de:** T1.05
- **Modificar:** `Infrastructure/Persistencia/Configuraciones.cs` (`MensajeConfig`):
  - `TipoSaliente` como `int`;
  - `OpcionesJson` como `nvarchar(max)`;
  - `ClaveIdempotencia` como `HasMaxLength(150)`;
  - `HasIndex(x => x.ClaveIdempotencia, "IX_Mensajes_ClaveIdempotencia").IsUnique().HasFilter("[ClaveIdempotencia] IS NOT NULL")`;
  - `HasIndex(x => new { x.EstadoEntrega, x.FechaEnvio }, "IX_Mensajes_EnCola").HasFilter("[EstadoEntrega] = 6")`.
- **Migración:** `ColaDeEnvios`.
- **Hecho cuando:** la migración se genera, se revisa y `dotnet ef database update` aplica en local
  (si hay SQL).

#### T1.07 · Cola de envíos: servicio

- **Brecha:** ARQ-03 · **Agente:** `backend-datos` · **Depende de:** T1.06
- **Modificar:** `Infrastructure/Servicios/MensajeService.cs`:
  - `EncolarSalienteAsync`: inserta con `Direccion = Saliente` y `EstadoEntrega = EnCola`,
    serializa parámetros y opciones. Ante violación de unicidad sobre la clave (errores 2601 y
    2627), `Detach` y devuelve `null`.
  - `TomarLoteEnColaAsync`: selecciona `EnCola` por `FechaEnvio`, los pasa a `Enviando` y guarda.
    Hay un solo Worker (V24), así que no hace falta reserva por fila.
  - `RecuperarEnviandoVencidosAsync`: los `Enviando` más viejos que la antigüedad pasan a
    `Fallido`, `Ambiguo` y sin próximo intento.
  - `ActualizarEstadoEntregaAsync`: reemplazar `nuevo <= mensaje.EstadoEntrega` por la función
    privada `OrdenAvance(EstadoEntrega)`, con el orden EnCola < Enviando < Pendiente < Enviado <
    Entregado < Leido y Fallido especial.
- **Crear:** pruebas de encolar dos veces con la misma clave (una sola fila) y de acuse sobre
  `Enviando`.
- **Hecho cuando:** las pruebas pasan.

#### T1.08 · Validador de envío y caso de despacho

- **Brecha:** ARQ-03 · **Agente:** `integracion-whatsapp` · **Depende de:** T1.07, T0.03
- **Crear**
  - `Application/Casos/ValidadorEnvio.cs`: extraído de `ReintentoEnvios.MotivoParaNoReintentarAsync`,
    con `Task<string?> MotivoParaNoEnviarAsync(Mensaje m, CancellationToken ct)`. Revisa opt-in,
    ventana si no es plantilla, y que la plantilla esté activa.
  - `Application/Casos/DespachoEnvios.cs`:
    - `Task<ResumenDespacho> ProcesarAsync(int maximo, CancellationToken ct)`:
      `RecuperarEnviandoVencidos`, luego `TomarLoteEnCola`, y por cada mensaje: validador,
      `EnviarSegunTipoAsync`, `MarcarEnvioLogradoAsync` o `MarcarEnvioFallidoAsync` (con
      `ProximoIntento` si es transitorio).
    - `EnviarSegunTipoAsync` decide según `TipoSaliente`: `EnviarTextoAsync`, `EnviarPlantillaAsync`
      (parámetros de `ParametrosPlantillaJson`), `EnviarBotonesAsync` o `EnviarListaAsync`
      (opciones de `OpcionesJson`).
- **Modificar**
  - `Application/Casos/ReintentoEnvios.cs`: usar `ValidadorEnvio` y `DespachoEnvios.EnviarSegunTipoAsync`,
    para reintentar también botones y listas.
  - `Infrastructure/RegistroDependencias.cs`: registrar ambos.
- **Hecho cuando:** hay pruebas del despacho de texto, plantilla inactiva (Permanente), ventana
  cerrada en texto (Permanente) y 503 (Transitorio con próximo intento).

#### T1.09 · Worker: bucle de despacho

- **Brecha:** ARQ-03 · **Agente:** `backend-datos` · **Depende de:** T1.08
- **Crear:** `Worker/ServicioDespachoEnvios.cs`, un BackgroundService con el patrón de
  `ServicioReintentoEnvios`: latido, `EsperaSegura`, un ámbito por lote.
- **Modificar**
  - `Worker/OpcionesWorker.cs`: `IntervaloDespachoSegundos` (2), `TamanoLoteDespacho` (20),
    `TimeoutEnviandoSegundos` (120).
  - `Worker/Latido.cs` (`ServiciosVigilados`): `DespachoEnvios`.
  - `Worker/Program.cs`: `AddHostedService<ServicioDespachoEnvios>()` **después** de la guardia.
  - `Worker/appsettings.json`.
  - `Api/Salud/ChequeoWorker.cs`: incluir el latido nuevo.
  - `tests/.../Escenarios/ArnesEscenario.cs`: implementar el paso `Despachar()`.
- **Hecho cuando:** `SaludWorkerTests` cubre el latido nuevo.

#### T1.10 · El ejecutor encola en lugar de enviar

- **Brecha:** ARQ-03 · **Agente:** `reglas-negocio` · **Depende de:** T1.09
- **Modificar**
  - `Domain/Reglas/ContextoRegla.cs`: `public string ClaveEjecucion { get; init; } = Guid.NewGuid().ToString("N");`
  - `Application/Casos/EjecutorAcciones.cs`:
    - numerar las acciones (índice de la lista);
    - `EnviarPlantillaAsync`, `EnviarTextoLibreAsync`, `EnviarMenuAsync` y `EnviarLinkJobFormsAsync`
      llaman a `mensajes.EncolarSalienteAsync(..., $"{contexto.ClaveEjecucion}:{indice}", ...)`;
    - se quita `IWhatsAppProvider` del constructor;
    - se mantienen los chequeos de opt-in y ventana como defensa.
  - `Application/Reglas/IFabricaContextoRegla.cs` e `Infrastructure/Servicios/FabricaContextoRegla.cs`:
    - los métodos `Para*` reciben `string claveEjecucion`;
    - `ProcesadorOutbox` pasa `evt:{EventoId}`;
    - `BarridoTiempo` pasa `barrido:{conversacionId}:{ahora:yyyyMMddHHmm}`. Esa clave solo
      desduplica dentro del mismo minuto; la unicidad real del barrido la dan los sellos de cada
      regla.
  - Pruebas `tests/.../Casos/*`: agregar el paso `Despachar()` o `DespachoEnvios.ProcesarAsync`
    donde se asertan envíos.
- **Hecho cuando:** todas las pruebas pasan. `EjecutorAcciones` no referencia `IWhatsAppProvider`.

#### T1.11 · Respuesta del analista con fila previa e idempotencia

- **Brecha:** ARQ-03 · **Agente:** `bandeja-blazor` · **Depende de:** T1.10
- **Modificar**
  - `Contracts/Bandeja/PeticionesBandeja.cs`: `PeticionResponder` suma `Guid ClaveIdempotencia`.
  - `Application/Casos/EnvioAnalista.cs`:
    1. Encolar con la clave `ana:{ClaveIdempotencia}` y `analistaId`. Si la clave existe, devolver
       el resultado del mensaje existente.
    2. Pasar a `Enviando` y enviar en línea (V14) con `DespachoEnvios.EnviarSegunTipoAsync`.
    3. Marcar el resultado.
    4. `RegistrarRespuestaAnalistaAsync` si salió.
  - `Api/Controllers/ConversacionesController.cs`: pasar la clave.
  - `Frontend/Components/Bandeja/CajaRespuesta.razor`: generar `Guid` al abrir o redactar, y
    regenerarlo solo tras un envío exitoso.
  - `Frontend/Servicios/ClienteApi.cs`.
- **Hecho cuando:** `EnvioAnalistaTests` confirma que dos llamadas con la misma clave dan un mensaje
  y un envío.

#### T1.12 · Consumidor de outbox transaccional y motor estricto

- **Brecha:** COR-04 (C5) · **Agente:** `backend-datos` (V35) · **Depende de:** T1.10
- **Modificar**
  - `Worker/ConsumidorOutbox.cs` (`IntentarAsync`):
    `await unidad.EjecutarAsync(async c => { await procesador.ProcesarAsync(evento, c); await eventos.MarcarProcesadoAsync(evento.EventoId, c); }, ct)`.
  - `Application/Reglas/MotorReglas.cs`: quitar el `catch` que continúa; registrar y **relanzar**.
    Actualizar el comentario citando V35.
  - `tests/.../Reglas/MotorReglasTests.cs`: la prueba que esperaba continuar pasa a esperar la
    excepción.
- **Crear:** `tests/.../Escenarios/E16SinDuplicadosTests.cs`:
  - procesar el mismo evento dos veces deja 1 mensaje y 1 envío;
  - una excepción inyectada en la auditoría tras la acción de envío, seguida del reintento, deja 1
    envío. Con SQL, además, 0 filas residuales del primer intento.
- **Hecho cuando:** E16 pasa.

#### T1.13 · Alertas operativas: modelo y servicio

- **Brecha:** ARQ-09 · **Agente:** `backend-datos` (V32) · **Depende de:** T1.01
- **Crear**
  - Entidad `AlertaOperativa` en `Domain/Entidades/Sistema.cs`, más `static class TiposAlerta`:
    `PlantillaNoAprobada`, `VacanteSinFormulario`, `MenuSinOpciones`, `CuentaSinTitular`,
    `CuentaSinRespaldo`, `CuentaDesactivadaConConversaciones`.
  - `Infrastructure/Servicios/AlertaOperativaService.cs`.
- **Modificar**
  - `Domain/Interfaces/ServiciosDominio.cs`: `IAlertaOperativaService` (firma en `03` §ARQ-09).
  - `Infrastructure/Persistencia/Configuraciones.cs`: `AlertaOperativaConfig` con el índice único
    filtrado (`Tipo`, `Clave`) `WHERE [FechaResuelta] IS NULL`.
  - `RrhhDbContext.cs`, `RegistroDependencias.cs`.
- **Migración:** `AlertasOperativas`.
- **Hecho cuando:** registrar dos veces la misma alerta abierta deja una fila con 2 ocurrencias.

#### T1.14 · Alertas en lugar de eventos huérfanos

- **Brecha:** ARQ-09 (M1) · **Agente:** `backend-datos` · **Depende de:** T1.13, T1.10
- **Modificar**
  - `Application/Casos/EjecutorAcciones.cs`: reemplazar `PublicarAsync(EnvioOmitidoSinPlantilla | VacanteSinFormulario | MenuSinOpciones | MenuTruncado)`
    por `alertas.RegistrarAsync(...)`. Las claves son `plantilla:{clave}`, `hc:{id}` y `menu`, y el
    detalle no lleva datos personales.
  - `Application/Reglas/Implementaciones/R15OptInYVentana.cs`: dejar de publicar
    `EnvioRequierePlantilla`, sustituyéndolo por una señal para `EnvioAnalista`. Opciones: acción
    nueva `RequierePlantilla()`, o mantener el `PublicarEvento` sin persistirlo en `EnvioAnalista`.
    **Elegir la acción `RequierePlantilla`** y eliminar la publicación.
  - `Application/Casos/EnvioAnalista.cs`: detectar `RequierePlantilla`.
  - `Application/Casos/RecepcionWebhook.cs` (`TiposEvento`): borrar las 5 constantes huérfanas.
  - **Migración de datos:** en la misma tarea, SQL en una migración nueva `LimpiezaEventosHuerfanos`
    que borra los `EventosSistema` pendientes de esos 5 tipos.
- **Crear:** `Api/Salud/ChequeoAlertas.cs`, que da `Degraded` si hay alertas abiertas de
  `PlantillaNoAprobada` o `VacanteSinFormulario`, y registrarlo en `Api/Program.cs`.
- **Hecho cuando:** `R09EnvioLinkTests` (vacante sin formulario) aserta una alerta en lugar de un
  evento, y las pruebas de la R15 se actualizan.

---

### BLOQUE 2 — Modelo y contexto

#### T2.01 · Calendario laboral puro

- **Brecha:** ARQ-08 · **Agente:** `arquitecto` para ubicarlo (V31), luego `backend-datos` ·
  **Depende de:** T0.02
- **Crear**
  - `Domain/Calendario/ZonaHorariaPeru.cs`, movido desde Infrastructure con el mismo contenido.
  - `Domain/Calendario/CalendarioLaboral.cs`, con los métodos de `03` §ARQ-08. La lógica de
    `MinutosHabilesEntre` y `Describir` se mueve desde `HorarioAtencionService`.
  - `tests/.../Dominio/CalendarioLaboralTests.cs`: casos del viernes 20:00 (inicio viernes 18:00,
    apertura lunes 09:00), jornada partida, sin tramos (null) y cruce de medianoche.
- **Modificar:** borrar `Infrastructure/Servicios/ZonaHorariaPeru.cs` y actualizar los `using`.
- **Hecho cuando:** las pruebas nuevas y `HorarioAtencionServiceTests` pasan.

#### T2.02 · Fachada de horario

- **Brecha:** ARQ-08 · **Agente:** `backend-datos` · **Depende de:** T2.01
- **Modificar**
  - `Domain/Interfaces/ServiciosDominio.cs` (`IHorarioAtencionService`):
    `Task<DateTime?> InicioPeriodoFueraDeHorarioAsync(int? cuentaId, DateTime momentoUtc, CancellationToken ct)`
    y `Task<DateTime?> ProximaAperturaAsync(...)`.
  - `Infrastructure/Servicios/HorarioAtencionService.cs`: cargar los tramos (cuenta o general) y
    delegar en `CalendarioLaboral`. Quitar la lógica duplicada.
  - `Reporting/Persistencia/ReportingDbContext.cs`: `DbSet<HorarioAtencion> HorariosAtencion`
    (mapeo de solo lectura).
- **Hecho cuando:** las pruebas pasan.

#### T2.03 · Estados de conversación: dominio

- **Brecha:** ARQ-05 · **Agente:** `backend-datos` (V30) · **Depende de:** T0.02
- **Modificar**
  - `Domain/Enums/Enumeraciones.cs`: `EstadoConversacion.EnMenuBot = 6`; comentario en `Cerrada`
    («reservado, sin uso»).
  - `Domain/Entidades/Conversaciones.cs` (`Conversacion`): `int IntentosMenuFallidos` y los
    `DateTime?` `FechaTextoNoReconocido`, `FechaPendienteDesde`, `FechaAvisoPendiente`,
    `FechaEscalamiento`, `FechaAvisoSegundoNivel`, `FechaAvisoFueraHorario`. El estado inicial pasa
    a `EnMenuBot`.
- **Hecho cuando:** compila.

#### T2.04 · Estados de conversación: migración

- **Brecha:** ARQ-05, ARQ-06 · **Agente:** `backend-datos` · **Depende de:** T2.03
- **Modificar:** `Configuraciones.cs` (`ConversacionConfig`):
  `HasIndex(x => new { x.Estado, x.FechaUltimaActividad }, "IX_Conversaciones_EstadoActividad")`.
- **Migración:** `SeguimientoConversacion`. Agregar al final de `Up` el SQL:

  ```sql
  UPDATE c SET Estado = 6
  FROM Conversaciones c
  WHERE c.Estado = 3
    AND NOT EXISTS (
      SELECT 1 FROM Auditoria a
      WHERE a.EntidadTipo = 'Conversacion'
        AND a.EntidadId = CAST(c.ConversacionId AS nvarchar(50))
        AND a.Accion = 'DerivadaABandejaGeneral');

  UPDATE Conversaciones SET FechaPendienteDesde = FechaUltimaActividad WHERE Estado = 3;
  ```

  En `Down`, `UPDATE Conversaciones SET Estado = 3 WHERE Estado = 6` antes de borrar las columnas.
- **Hecho cuando:** hay una prueba SQL de la migración de datos (T1.02).

#### T2.05 · Estados de conversación: servicio, acceso y bandeja

- **Brecha:** ARQ-05 (AL1, AL10) · **Agente:** `backend-datos` y `bandeja-blazor` ·
  **Depende de:** T2.04
- **Modificar**
  - `Infrastructure/Servicios/ConversacionService.cs`:
    - `ObtenerOCrearAsync`: estado `EnMenuBot`.
    - `ObtenerAccesoAsync`, con esta matriz:

      | Caso | Nivel |
      |---|---|
      | Quien atiende | `Total` |
      | `PendienteClasificar` con rol `Analista` | `Lectura` |
      | Sistemas | `Lectura` |
      | `EnMenuBot` | solo Sistemas `Lectura` |
      | El resto | `Ninguno` |

    - `LimpiarCuentaContextoAsync`: estado `EnMenuBot`, `IntentosMenuFallidos = 0` y
      `FechaTextoNoReconocido = null`.
    - `AsignarAnalistaAsync`: si el estado es `EnMenuBot` o `PendienteClasificar`, pasa a `Activa`
      y limpia `FechaPendienteDesde` y los intentos.
    - `ListarParaAnalistaAsync`: excluir `EnMenuBot`, además de `Archivada`.
  - `Application/Reglas/Implementaciones/R01Asignacion.cs` y `R14Ausencias.cs`: tratar `EnMenuBot`
    igual que `PendienteClasificar` al pasar a `Activa`.
  - `tests/.../Casos/VisibilidadTests.cs`, `FiltroAccesoConversacionTests.cs`: nueva matriz.
- **Crear:** `tests/.../Escenarios/E01…` (parte de estado): el primer mensaje deja `EnMenuBot` y no
  aparece en `ListarPendientesClasificarAsync`.
- **Hecho cuando:** las pruebas pasan. **Nota:** hasta T3.13 nadie puede responder en
  `PendienteClasificar`. Es aceptable dentro del bloque; no se publica una versión entre T2.05 y
  T3.14.

#### T2.06 · Desenlace de etapas y campos de postulación

- **Brecha:** ARQ-06, COR-11, FUN-08, FUN-10, FUN-11 · **Agente:** `backend-datos` ·
  **Depende de:** T0.02
- **Modificar**
  - `Domain/Enums/Enumeraciones.cs`: `EstadoPostulacion.Reingreso = 5`.
  - `Domain/Entidades/Postulantes.cs`:
    - `EtapaKanban.EstadoResultante` (`EstadoPostulacion?`);
    - `Postulacion.CierreCortesiaPendiente` (`bool`), `FechaCierreCortesia`,
      `FechaAvisoArchivado`, `FechaReingreso` (`DateTime?`).
  - `Configuraciones.cs`: conversión a `int` de `EstadoResultante`.
  - `DatosSemilla.cs`: etapa 4 con `EstadoResultante = Contratado`; etapa 5 con `Descartado`.
- **Migración:** `DesenlaceYCierre`, que genera `UpdateData` para las etapas 4 y 5. Verificarlo.
- **Hecho cuando:** compila y la migración está revisada.

#### T2.07 · Código de aviso por vacante (esquema)

- **Brecha:** ARQ-06, FUN-02 · **Agente:** `backend-datos` · **Depende de:** T0.02
- **Modificar**
  - `Domain/Entidades/Organizacion.cs` (`Hc`): `string? CodigoAviso`.
  - `Configuraciones.cs` (`HcConfig`): `HasMaxLength(12)` y
    `HasIndex(x => x.CodigoAviso, "IX_HC_CodigoAviso").IsUnique().HasFilter("[CodigoAviso] IS NOT NULL")`.
- **Migración:** `CodigoAvisoVacante`, con backfill SQL. Recorrer las filas con `CodigoAviso IS NULL`
  y asignar `UPPER(SUBSTRING(REPLACE(CONVERT(varchar(36), NEWID()), '-', ''), 1, 6))` en un bucle
  `WHILE` que reintenta ante colisión. Documentar en un comentario que el alfabeto definitivo sin
  ambiguos lo aplica el servicio a futuro.
- **Hecho cuando:** la migración aplica y no quedan vacantes sin código.

#### T2.08 · Transferencias: esquema de vencimiento

- **Brecha:** ARQ-06, FUN-07 · **Agente:** `backend-datos` · **Depende de:** T0.02
- **Modificar**
  - `Domain/Enums/Enumeraciones.cs`: `EstadoTransferencia.Vencida = 4` y `Retirada = 5`.
  - `Domain/Entidades/Conversaciones.cs` (`Transferencia`): `DateTime? FechaVencimiento`.
  - `Configuraciones.cs` (`TransferenciaConfig`):
    `HasIndex(x => x.ConversacionId, "IX_Transferencias_PendienteUnica").IsUnique().HasFilter("[Estado] = 1")`.
- **Migración:** `VencimientoTransferencias`. Antes de crear el índice, un SQL que deje una sola
  pendiente por conversación, rechazando las más viejas, por si hay duplicados históricos.
- **Modificar:** `ConversacionService.TransferirAsync`: capturar la violación de unicidad y
  traducirla al mismo `InvalidOperationException` de «ya hay una pendiente».
- **Hecho cuando:** hay una prueba SQL de dos transferencias simultáneas: una falla con mensaje de
  negocio.

#### T2.09 · Índices de outbox, respuestas únicas y retorno de ausencia

- **Brecha:** ARQ-06, ARQ-13, B9, FUN-12 · **Agente:** `backend-datos` · **Depende de:** T0.02
- **Modificar**
  - `Configuraciones.cs`:
    - `EventoSistemaConfig`: reemplazar `HasIndex(x => new { x.Estado, x.FechaCreacion })` por
      `HasIndex(x => new { x.Estado, x.Tipo, x.FechaCreacion }, "IX_EventosSistema_Cola")`;
    - `JobFormsRespuestaConfig`:
      `HasIndex(x => x.InvitacionId, "IX_JobFormsRespuestas_Invitacion").IsUnique().HasFilter("[InvitacionId] IS NOT NULL")`.
  - `Domain/Entidades/Organizacion.cs` (`Ausencia`): `DateTime? FechaAvisoRetorno`.
- **Migración:** `IndicesYRetorno`, con un SQL previo que detecta respuestas duplicadas por
  invitación. Si existen, **detenerse y reportar**; no borrar datos de postulantes sin autorización.
- **Hecho cuando:** la migración está revisada.

#### T2.10 · Parámetros de atención preferente

- **Brecha:** ARQ-06 §1.1, COR-17 · **Agente:** `reglas-negocio` · **Depende de:** T0.02
- **Modificar**
  - `Domain/Entidades/Sistema.cs` (`ClavesConfiguracion`): las 9 constantes nuevas de `03` §1.1.
  - `Infrastructure/Persistencia/DatosSemilla.cs`: agregarlas con valor y descripción, y cambiar la
    descripción de `envio.maximo_por_segundo` a «… por proceso emisor».
- **Migración:** `ParametrosAtencionPreferente`.
- **Hecho cuando:** `ParametrosReglasTests` confirma que todas las claves de `ClavesConfiguracion`
  están sembradas.

#### T2.11 · Contexto de reglas y acciones nuevas (dominio)

- **Brecha:** ARQ-07 · **Agente:** `reglas-negocio` · **Depende de:** T2.03, T2.06, T2.08, T1.10
- **Modificar**
  - `Domain/Reglas/ContextoRegla.cs`: propiedades de `03` §ARQ-07.
    - Record `PostulacionVigente` y enum `OrigenEleccion { Ninguna, Boton, CodigoAviso, NombreCuenta, Contexto }`.
    - Derivados `TieneProcesoVivo`, `CuentasVivas` y `DiasDesdeActividadAnterior`.
    - Marcar `[Obsolete]` `EstadosPostulaciones` y `DiasDesdeMensajeAnterior`; se borran en T3.09.
  - `Domain/Reglas/AccionRegla.cs`: records de la tabla de `03` §ARQ-07, más:
    - `enum MarcaConversacion { AvisoFueraHorario, AvisoSegundoNivel, AvisoPendiente }`;
    - `enum MarcaPostulacion { AvisoArchivado, CierreCortesiaEnviado, CierreCortesiaPendiente }`;
    - `RequierePlantilla()` (T1.14);
    - `MostrarMenuEmpresas` pasa a `(bool EsReintento, int Pagina = 1)`;
    - `EscalarARespaldo` suma `int AnalistaEsperadoId`.
  - `Domain/Entidades/IdsBoton.cs`: `PrefijoPagina = "pag_"`, `PrefijoProceso = "proc_"`,
    `OtraEmpresa = "otra_empresa"`, con sus `Para*` y `Leer*`.
- **Hecho cuando:** compila. Las reglas existentes siguen usando lo obsoleto sin romper.

#### T2.12 · Payload mínimo con instantánea previa

- **Brecha:** ARQ-07, ARQ-13 (C1, AL6) · **Agente:** `integracion-whatsapp` · **Depende de:** T2.11, T1.03
- **Modificar**
  - `Application/Casos/RecepcionWebhook.cs`: antes de `RegistrarEntradaAsync`, leer
    `conversacion.FechaUltimaActividad` (instantánea). Publicar
    `new { ConversacionId, MensajeId, IdBotonPulsado, FechaActividadAnterior }`, **sin** teléfono,
    contenido ni nombre.
  - `Application/Casos/ProcesadorOutbox.cs`: `PayloadMensajeEntrante(int ConversacionId, long MensajeId, string? IdBotonPulsado, DateTime? FechaActividadAnterior)`,
    pasado a la fábrica.
  - `IFabricaContextoRegla.ParaMensajeEntranteAsync`: suma los parámetros `mensajeId` y
    `fechaActividadAnterior`.
- **Hecho cuando:** `RecepcionWebhookTests` confirma que el payload no contiene el teléfono.

#### T2.13 · La fábrica carga el contexto nuevo

- **Brecha:** ARQ-07 · **Agente:** `reglas-negocio` · **Depende de:** T2.12, T2.02, T2.07
- **Modificar:** `Infrastructure/Servicios/FabricaContextoRegla.cs`:
  - `PostulacionesDelPostulante` (join con `Cuenta` y `Hc`);
  - `FechaActividadAnterior` (del parámetro);
  - `InicioPeriodoFueraHorario` y `ProximaApertura`;
  - `MinutosHabilesDesdeEscalamiento`, `EnPendiente` y `DesdeTextoNoReconocido`, con
    `IHorarioAtencionService.MinutosHabilesEntreAsync`;
  - `TransferenciaPendiente`;
  - `PaginaMenu` y `PostulacionElegidaId` desde `IdsBoton`;
  - `OrigenEleccion`: `Boton` si hubo botón de cuenta o vacante, `Contexto` si viene de la
    conversación. `CodigoAviso` y `NombreCuenta` se completan en T3.07.
  - Método nuevo `ParaTiempoPostulacionAsync(int postulacionId, string claveEjecucion, CancellationToken ct)`,
    que carga la postulación, la conversación más reciente del postulante y la cuenta.
- **Modificar:** `tests/.../Reglas/ConstructorContexto.cs`, con fluidez para las propiedades nuevas.
- **Hecho cuando:** las pruebas pasan.

#### T2.14 · El ejecutor implementa las acciones nuevas

- **Brecha:** ARQ-07, COR-03 (base) · **Agente:** `reglas-negocio` y `backend-datos` ·
  **Depende de:** T2.13, T1.14
- **Modificar**
  - `Domain/Interfaces/ServiciosDominio.cs` y las implementaciones en `Infrastructure/Servicios`:

    | Servicio | Métodos nuevos |
    |---|---|
    | `IConversacionService` | `SellarAsync(int id, MarcaConversacion marca)`, `RegistrarIntentoMenuAsync(int id, bool textoNoReconocido)`, `ReiniciarIntentosMenuAsync(int id)`, `DerivarAPendientesAsync(int id, string motivo)` (estado, `FechaPendienteDesde`, auditoría `DerivadaABandejaGeneral`), `ReactivarAsync(int id, EstadoConversacion nuevo)`, `TomarContextoDePostulacionAsync(int conversacionId, int postulacionId)` (cuenta y analista asignado de la postulación, o el titular si no hay) |
    | `IPostulacionService` | `ArchivarAsync(int postulacionId, string motivo)`, `SellarAsync(int postulacionId, MarcaPostulacion marca)` |
    | `IAnalistaService` | `ListarActivosPorRolAsync(RolAnalista rol)` |

  - `Application/Casos/EjecutorAcciones.cs`:
    - un `case` por acción nueva;
    - `NotificarRol` publica un `AnalistaNotificado` por analista del rol;
    - **`EnviarMensajeBot`**: con ventana abierta, encola texto; si no, con plantilla activa,
      encola plantilla; si no, alerta `PlantillaNoAprobada` y la acción se marca **no ejecutada**.
    - El ejecutor lleva `bool ultimoEnvioEncolado`; las acciones de sellado con
      `SoloSiSeEnvioAnterior = true` se saltan si es `false`. Agregar esa propiedad a
      `SellarPostulacion` y `MarcarRecordatorioJobForms`.
- **Crear:** `tests/.../Casos/EjecutorAccionesNuevasTests.cs`, con una prueba por acción.
- **Hecho cuando:** las pruebas pasan.

---

### BLOQUE 3 — Flujo del postulante

#### T3.01 · Mensajes del bot en texto libre dentro de la ventana (P1)

- **Brecha:** COR-03 (C3) · **Agente:** `reglas-negocio` (V36) · **Depende de:** T2.14
- **Crear:** `Application/Reglas/TextosBot.cs`, con constantes o métodos que devuelvan el mismo
  contenido que los borradores de `DatosSemilla`, parametrizados:
  - `ConfirmacionFormulario(nombre, vacante)`
  - `RecordatorioFormulario(nombre, vacante, enlace)`
  - `CierreCortesia(nombre, vacante)`
  - `VacanteCerrada(vacante)`
  - `FueraDeHorario(horario, proximaApertura)`
- **Modificar** (`EnviarPlantilla` pasa a `EnviarMensajeBot(texto, clave, parametros)`):
  - `Application/Reglas/Implementaciones/R09ConfirmacionJobForms.cs`
  - `R09SeguimientoJobForms.cs`: `MarcarRecordatorioJobForms(id) { SoloSiSeEnvioAnterior = true }`
  - `R12CierreCortesia.cs`: provisorio hasta T4.09
  - `R20VacanteCerrada.cs`
- **Crear:** `tests/.../Escenarios/E04ConfirmacionSinPlantillasTests.cs` (parte 1): con plantillas
  inactivas, completar el formulario dentro de las 24 h, consumir la outbox y despachar, da un texto
  de confirmación. Otra prueba: recordatorio con la ventana cerrada y sin plantilla deja una alerta
  y la invitación **sin** sellar; al activar la plantilla en la prueba, sale y se sella.
- **Hecho cuando:** los escenarios pasan.

#### T3.02 · R3 reescrita

- **Brecha:** FUN-04, COR-01 (C1) · **Agente:** `reglas-negocio` · **Depende de:** T3.01
- **Modificar**
  - `Application/Reglas/Implementaciones/R03FueraDeHorario.cs`: implementación de `03` §FUN-04.
    `Aplica` usa `FechaAvisoFueraHorario` e `InicioPeriodoFueraHorario`. Resultado:
    `EnviarMensajeBot(TextosBot.FueraDeHorario(...), ClavesPlantilla.FueraDeHorario, [descripcion])`
    y `SellarConversacion(AvisoFueraHorario) { SoloSiSeEnvioAnterior = true }`.
    - Quitar el literal de 8 h y `horario.descripcion`.
    - Agregar `SoloSiSeEnvioAnterior` a `SellarConversacion` si no se hizo en T2.11.
  - `Infrastructure/Servicios/FabricaContextoRegla.cs`: descripción del horario en el contexto:
    propiedad `DescripcionHorario` (agregarla a `ContextoRegla`).
  - `tests/.../Reglas/ReglasFlujoTests.cs` (`R03FueraDeHorarioTests`): rehacer los casos.
- **Crear:** `tests/.../Escenarios/E01FueraDeHorarioTests.cs`: viernes 20:00 más tres mensajes dan
  un aviso que menciona «lunes» y «09:00»; el lunes a las 19:00 da otro aviso. **Verificar que este
  escenario falla contra el `R03` anterior** (checkout de la versión previa o una prueba
  documentada).
- **Hecho cuando:** E01 pasa.

#### T3.03 · El menú solo ofrece opciones válidas

- **Brecha:** COR-09 (AL5) · **Agente:** `backend-datos` · **Depende de:** T1.14
- **Modificar**
  - `Domain/Interfaces/ServiciosDominio.cs`: renombrar `ListarConVacantesAbiertasAsync` a
    `ListarMenuAsync`, y documentarlo.
  - `Infrastructure/Servicios/CuentaService.cs`: filtro `c.Activo`, con titular
    (`Asignaciones.Any(a => !a.EsBackup && a.Analista.Activo)`) y vacantes
    `Any(v => v.Estado == Abierta && v.UrlJobForms != null && v.UrlJobForms != "")`.
    `CrearVacanteAsync`: si `UrlJobForms` está vacía, `alertas.RegistrarAsync(VacanteSinFormulario, $"hc:{id}", titulo)`
    después de guardar.
  - `Infrastructure/Servicios/FabricaContextoRegla.cs` (`VacantesAbiertasAsync`): filtrar las que
    no tienen URL.
  - `Application/Casos/EjecutorAcciones.cs`: usar `ListarMenuAsync`.
  - `README.md`: la frase sobre el menú queda correcta.
- **Hecho cuando:** `AdministracionCuentasTests` confirma que una cuenta sin titular o sin
  formulario no aparece.

#### T3.04 · R9 enlace sin menú repetido

- **Brecha:** COR-02 (C2) · **Agente:** `reglas-negocio` · **Depende de:** T2.13, T3.03
- **Modificar:** `Application/Reglas/Implementaciones/R09EnvioLink.cs`, según `03` §COR-02.
- **Crear:** `tests/.../Escenarios/E04MenuVacantesTests.cs`:
  - dos vacantes: botón de cuenta, luego menú de vacantes, botón de vacante, enlace, formulario,
    tres textos. **Total de menús de vacantes = 1.**
  - Una cuenta con un hilo vivo abre una segunda vacante y el postulante escribe: 0 menús.
- **Hecho cuando:** E04 pasa. `R09EnvioLinkTests` sigue verde.

#### T3.05 · R19 con contador persistente

- **Brecha:** COR-06 (AL2) · **Agente:** `reglas-negocio` · **Depende de:** T2.14, T2.10
- **Modificar**
  - `Application/Reglas/Implementaciones/R19FallbackMenu.cs`: según `03` §COR-06, con
    `menu.reintentos_permitidos`; quitar la constante.
  - `Infrastructure/Servicios/FabricaContextoRegla.cs`: borrar `ContarIntentosMenuAsync` e
    `IntentosMenuFallidos` sale de `conversacion.IntentosMenuFallidos`.
  - `R01Asignacion.cs` y `R14Ausencias.cs`: agregar `ReiniciarIntentosMenu()` cuando asignan.
- **Crear:** `tests/.../Escenarios/E05MenuNoReconocidoTests.cs` (parte 1): «Hola» da el menú;
  «xx» da el reintento; «yy» pasa a `PendienteClasificar` con `FechaPendienteDesde`. Un hilo con 30
  entrantes históricos al que se le limpió el contexto recibe «zz» y obtiene **menú**, no
  derivación.
- **Hecho cuando:** E05 (parte 1) pasa.

#### T3.06 · Menú de empresas paginado

- **Brecha:** FUN-03 · **Agente:** `integracion-whatsapp` · **Depende de:** T3.05
- **Modificar**
  - `Application/Casos/EjecutorAcciones.cs` (`MostrarMenuEmpresasAsync`): paginación de 9 más
    «Ver más empresas» y «Volver al inicio» en la última página. Constante
    `FilasPorPaginaConNavegacion = 9`, comentada como límite de Meta. Quitar la alerta y el evento
    de truncado.
  - `R19FallbackMenu.cs`: `PaginaMenu > 0` da `MostrarMenuEmpresas(false, PaginaMenu)` sin
    registrar intento.
- **Crear:** `tests/.../Escenarios/E03MenuPaginadoTests.cs`, con 20 cuentas sembradas.
- **Hecho cuando:** E03 pasa.

#### T3.07 · Código de aviso y reconocimiento por texto

- **Brecha:** FUN-02 · **Agente:** `reglas-negocio` y `backend-datos` · **Depende de:** T3.06, T2.07
- **Crear:** `Domain/Entidades/CodigoAviso.cs`:
  - `const string Alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"`;
  - `static string Generar(Random r)`;
  - `static bool EsValido(string)`;
  - `static IEnumerable<string> Candidatos(string texto)`: tokens en mayúsculas, sin tildes, de 4 a
    12 caracteres alfanuméricos.
- **Modificar**
  - `Domain/Interfaces/ServiciosDominio.cs` (`ICuentaService`):
    `Task<Hc?> BuscarVacantePorCodigoAsync(IReadOnlyCollection<string> candidatos, CancellationToken ct)`
    y `Task<Cuenta?> BuscarCuentaDeMenuPorNombreAsync(string textoNormalizado, CancellationToken ct)`.
  - `Infrastructure/Servicios/CuentaService.cs`: implementarlos. `CrearVacanteAsync` genera el
    código único, reintentando ante colisión.
  - `Infrastructure/Servicios/FabricaContextoRegla.cs` (`ParaMensajeEntranteAsync`): sin botón y
    con contenido, primero el código (`OrigenEleccion.CodigoAviso`) y luego el nombre de cuenta
    (`NombreCuenta`). Leer el contenido del mensaje por `mensajeId` (T2.12).
  - `R19FallbackMenu.Aplica`: no aplica si `OrigenEleccion` no es `Ninguna`.
- **Crear:** `tests/.../Escenarios/E02CodigoAvisoTests.cs`: «Hola, postulo a K7M2QX» da el enlace
  del formulario sin menú. Con el código de una vacante cerrada, entra la R20.
- **Hecho cuando:** E02 pasa.

#### T3.08 · Enlace de aviso en administración

- **Brecha:** FUN-02 · **Agente:** `bandeja-blazor` · **Depende de:** T3.07
- **Pregunta al usuario:** valor de `WhatsApp:NumeroPublico` (§1.4). Si no está, se deja vacío y la
  pantalla lo advierte.
- **Crear:** `Api/Configuracion/OpcionesWhatsApp.cs` (`NumeroPublico`).
- **Modificar**
  - `Api/Program.cs`: `Configure<OpcionesWhatsApp>`.
  - `Api/appsettings.json`: `"WhatsApp": { "NumeroPublico": "" }`.
  - `Contracts/Administracion/ContratosAdministracion.cs` (o donde esté `VacanteResumen`): suma
    `string? CodigoAviso`; nuevo record `EnlaceAviso(string Codigo, string? Enlace)`.
  - `Api/Controllers/VacantesController.cs`: `AResumen` con el código; `GET /hc/{id}/enlace-aviso`
    con el mismo permiso que editar la vacante.
  - `Frontend/Servicios/ClienteApi.cs`: `EnlaceAvisoAsync`.
  - `Frontend/Components/Pages/Vacantes.razor`: columna «Código», botón «Copiar enlace» y aviso si
    `Enlace` es nulo.
- **Hecho cuando:** hay una prueba de API del enlace y un checklist manual en la bandeja.

#### T3.09 · R9 repregunta con continuidad

- **Brecha:** COR-07 (AL3) · **Agente:** `reglas-negocio` · **Depende de:** T2.13, T3.05
- **Modificar**
  - `Application/Reglas/Implementaciones/R09RepreguntaEmpresa.cs`: según `03` §COR-07, usando
    `DiasDesdeActividadAnterior` y `PostulacionesDelPostulante`.
  - `R16Archivado.cs`: migrar a `PostulacionesDelPostulante`.
  - `Domain/Reglas/ContextoRegla.cs` e `Infrastructure/Servicios/FabricaContextoRegla.cs`: **borrar**
    `EstadosPostulaciones` y `DiasDesdeMensajeAnterior`, junto con sus consultas.
  - `tests/.../Casos/R09RepreguntaEmpresaTests.cs`: rehacer.
- **Crear:** `tests/.../Escenarios/E11E12RepreguntaTests.cs`:
  - `EnProceso` escribe a los 5 días: sigue `Activa` con su analista y sin menú;
  - `Descartado` escribe a los 5 días: `EnMenuBot` y recibe el menú.
- **Hecho cuando:** E11 y E12 pasan.

#### T3.10 · R14 respeta lo asignado

- **Brecha:** COR-08 (AL4) · **Agente:** `reglas-negocio` · **Depende de:** T2.14
- **Modificar:** `Application/Reglas/Implementaciones/R14Ausencias.cs`, según `03` §COR-08, con
  `DerivarAPendientes` si no hay respaldo.
- **Crear:** `tests/.../Escenarios/E10AusenciaTransferenciaTests.cs`, más casos en
  `ReglasAsignacionTests.cs`.
- **Hecho cuando:** E10 pasa.

#### T3.11 · Aviso multi-cuenta una sola vez

- **Brecha:** COR-10 (AL9) · **Agente:** `reglas-negocio` · **Depende de:** T3.10
- **Crear:** `Application/Reglas/AvisoMultiCuenta.cs`, con
  `static IEnumerable<AccionRegla> Acciones(ContextoRegla ctx, int analistaDestinoId, bool notificarOtrasCuentas)`.
- **Modificar**
  - `R01Asignacion.cs`: aviso solo si asigna o si el disparador es `JobFormsCompletado`. En ese
    caso, `notificarOtrasCuentas = true`, usando `PostulacionesDelPostulante` vivas de otras
    cuentas y su `AnalistaAsignadoId`.
  - `R14Ausencias.cs`: usar el helper.
- **Crear:** prueba con 3 entrantes seguidos: 1 aviso. Formulario en la cuenta B: avisos a A y a B.
- **Hecho cuando:** pasa.

#### T3.12 · Kanban con desenlace explícito y reversible

- **Brecha:** COR-11 (AL7) · **Agente:** `backend-datos` · **Depende de:** T2.06
- **Modificar**
  - `Domain/Interfaces/ServiciosDominio.cs`: `record ResultadoMovimiento(EstadoPostulacion Anterior, EstadoPostulacion Nuevo, bool Aplicado)`;
    `MoverEtapaKanbanAsync` devuelve `Task<ResultadoMovimiento>`.
  - `Infrastructure/Servicios/PostulacionService.cs`: lógica de `03` §COR-11; borrar los
    `StartsWith`; `EtapaFinalDescartadoAsync` por `EstadoResultante`.
  - `Application/Casos/AccionesBandeja.cs`: publicar el descarte solo si pasó a `Descartado`, dentro
    de `IUnidadTrabajo`.
  - `Application/Casos/EjecutorAcciones.cs` (`MoverEtapaKanban`): adaptar la firma.
- **Crear:** `tests/.../Escenarios/E14KanbanTests.cs` (parte de estado): Descartado → Entrevista da
  `EnProceso`; volver a Descartado publica **un** evento nuevo solo si antes no lo estaba. La
  unicidad del cierre se completa en T4.09.
- **Hecho cuando:** pasa.

#### T3.13 · Tomar conversación: servicio y bloqueos

- **Brecha:** FUN-01 (AL1) · **Agente:** `backend-datos` · **Depende de:** T2.05, T1.01
- **Modificar**
  - `Domain/Interfaces/ServiciosDominio.cs` (`IConversacionService`):
    `Task<Conversacion> TomarAsync(int conversacionId, int analistaId, int cuentaId, CancellationToken ct)`.
  - `Infrastructure/Servicios/ConversacionService.cs`: implementar según `03` §FUN-01, con
    `ICuentaService` inyectado para el acceso a la cuenta. `DbUpdateConcurrencyException` da
    `ConflictoConcurrenciaException` (nueva, en Domain) y otro estado da `InvalidOperationException`.
  - `Application/Casos/EnvioAnalista.cs`: si el estado es `PendienteClasificar` o `EnMenuBot`,
    `Enviado = false` con el motivo «Tomá la conversación antes de responder».
  - `ConversacionService.TransferirAsync` y `ConversacionesController.Marcar`: mismo rechazo.
- **Hecho cuando:** hay pruebas de servicio para tomar bien, tomar ajena (sin acceso a la cuenta) y
  tomar en otro estado.

#### T3.14 · Tomar conversación: Api y bandeja

- **Brecha:** FUN-01 · **Agente:** `bandeja-blazor` · **Depende de:** T3.13
- **Crear:** `Api/Seguridad/PermiteTomarAttribute.cs`.
- **Modificar**
  - `Contracts/Bandeja/PeticionesBandeja.cs`: `record PeticionTomar(int CuentaId)`.
  - `Api/Seguridad/FiltroAccesoConversacion.cs`: si la acción tiene `[PermiteTomar]` y el nivel es
    `Lectura`, dejar pasar. `Api/Seguridad/ResultadoAcceso.cs`, si hace falta.
  - `Api/Controllers/ConversacionesController.cs`: `[HttpPost("{id:int}/tomar")] [PermiteTomar]`,
    que responde 200 con el resumen, 409 si hubo conflicto y 403 sin acceso a la cuenta.
  - `Frontend/Servicios/ClienteApi.cs`: `TomarAsync`.
  - `Frontend/Components/Bandeja/PanelChat.razor`: con estado `PendienteClasificar`, selector de
    cuentas (`ClienteApi.CuentasDeAnalistaAsync`) y botón «Tomar»; `CajaRespuesta` oculta. Tras
    tomar, `AlCambiar`.
- **Crear:** `tests/.../Escenarios/E07TomarTests.cs`: dos tomas concurrentes dan un éxito y un
  conflicto (SQL). Responder sin tomar da no enviado.
- **Hecho cuando:** E07 pasa y hay un checklist manual en la bandeja.

#### T3.15 · Escalamiento sin carrera

- **Brecha:** COR-13 (M4) · **Agente:** `backend-datos` · **Depende de:** T2.14
- **Modificar**
  - `IConversacionService.EscalarAsync(int conversacionId, int analistaEsperadoId, int analistaRespaldoId, string motivo, CancellationToken ct)`.
  - `ConversacionService.EscalarAsync`: revalida la respuesta y el analista esperado; sella
    `FechaEscalamiento`.
  - `R02Escalamiento.cs`: pasar `AnalistaEsperadoId = ctx.Conversacion.AnalistaAtendiendoId` y quitar
    `CambiarEstadoConversacion(Escalada)`.
  - `EjecutorAcciones.cs`.
- **Hecho cuando:** hay una prueba de respuesta registrada entre el contexto y la ejecución: no
  escala.

---

### BLOQUE 4 — Seguimiento por tiempo

#### T4.01 · Segundo nivel de escalamiento

- **Brecha:** FUN-05 · **Agente:** `reglas-negocio` · **Depende de:** T3.15, T2.13
- **Crear:** `Application/Reglas/Implementaciones/R02SegundoNivel.cs` (prioridad 26).
- **Modificar**
  - `Infrastructure/RegistroDependencias.cs` (`AgregarReglas`) y `EntornoDeReglas`: registrarla.
  - `ConversacionService.cs`: `ListarPendientesSegundoNivelAsync(int maximo)`.
    `RegistrarRespuestaAnalistaAsync` limpia `FechaAvisoSegundoNivel`.
  - `Worker/ServicioBarridoTiempo.cs` (`ReunirCandidatasAsync`): sumar candidatas.
  - `tests/.../Infraestructura/RegistroDependenciasTests.cs`: códigos esperados.
- **Crear:** `tests/.../Escenarios/E08SegundoNivelTests.cs`, con el reloj simulado en horario
  hábil.
- **Hecho cuando:** E08 pasa.

#### T4.02 · Marca «vencida» en la bandeja

- **Brecha:** FUN-05 · **Agente:** `bandeja-blazor` · **Depende de:** T4.01
- **Modificar**
  - `Contracts/Bandeja/ContratosBandeja.cs` (`ConversacionResumen`): suma `bool Vencida`.
  - `Api/Mapeo/MapeoBandeja.cs`.
  - `Frontend/Components/Bandeja/ListaConversaciones.razor` y `wwwroot/bandeja.css`: distintivo.
- **Hecho cuando:** hay una prueba de mapeo y un checklist manual.

#### T4.03 · Derivación por silencio y plazo de «Sin clasificar»

- **Brecha:** FUN-06 · **Agente:** `reglas-negocio` · **Depende de:** T3.05, T2.13
- **Crear:** `Application/Reglas/Implementaciones/R19DerivacionPorSilencio.cs` y
  `R19AvisoPendiente.cs`.
- **Modificar**
  - `ConversacionService.cs`: `ListarPendientesDerivacionMenuAsync` y
    `ListarPendientesAvisoClasificacionAsync`.
  - `ServicioBarridoTiempo.cs`, `RegistroDependencias.cs` y `EntornoDeReglas`.
- **Crear:** `tests/.../Escenarios/E05MenuNoReconocidoTests.cs` (parte 2: aviso a Jefatura a las
  2 h hábiles) y `E06SilencioTests.cs`.
- **Hecho cuando:** E05 y E06 pasan.

#### T4.04 · Vencimiento de transferencias

- **Brecha:** FUN-07 · **Agente:** `backend-datos` y `reglas-negocio` · **Depende de:** T2.08, T2.13
- **Crear:** `Application/Reglas/Implementaciones/R08VencimientoTransferencia.cs`.
- **Modificar**
  - `ConversacionService.TransferirAsync`: inyectar `IAusenciaService` e
    `IHorarioAtencionService`; rechazar destino ausente; calcular `FechaVencimiento` con
    `CalendarioLaboral`, sumando minutos hábiles. Agregar a `CalendarioLaboral`
    `DateTime SumarMinutosHabiles(tramos, desdeUtc, minutos)`, con su prueba.
  - `ConversacionService.cs`: `VencerTransferenciaAsync(int transferenciaId)` (estado y avisos) y
    `ListarConversacionesConTransferenciaVencidaAsync(DateTime ahora, int maximo)`.
  - `EjecutorAcciones.cs` (`VencerTransferencia`), `ServicioBarridoTiempo.cs`,
    `RegistroDependencias.cs`.
- **Crear:** `tests/.../Escenarios/E09TransferenciaVenceTests.cs`.
- **Hecho cuando:** E09 pasa.

#### T4.05 · Retiro de transferencias y bandeja

- **Brecha:** FUN-07 · **Agente:** `bandeja-blazor` · **Depende de:** T4.04
- **Crear:** `Frontend/Components/Bandeja/TransferenciasEnviadas.razor`.
- **Modificar**
  - `IConversacionService` y su implementación: `RetirarTransferenciaAsync` y
    `ListarTransferenciasEnviadasPendientesAsync(int analistaOrigenId)`.
  - `Contracts/Bandeja/PeticionesBandeja.cs`: `TransferenciaPendiente` suma `DateTime? FechaVencimiento`;
    nuevo `record TransferenciaEnviada(int TransferenciaId, int ConversacionId, string AnalistaDestino, string? NombrePostulante, DateTime FechaUtc, DateTime? FechaVencimiento)`.
    `AnalistaResumen` suma `bool Ausente`.
  - `Api/Controllers/TransferenciasController.cs`: `POST {id}/retirar` y `GET enviadas`.
  - `Api/Controllers/AnalistasController.cs` (`Listar`): calcular `Ausente`.
  - `Frontend/Servicios/ClienteApi.cs`.
  - `Frontend/Components/Bandeja/TransferenciasRecibidas.razor`: mostrar el vencimiento.
  - `Frontend/Components/Bandeja/AccionesRapidas.razor`: excluir ausentes.
  - `Frontend/Components/Pages/Bandeja.razor`: incluir el componente nuevo.
- **Hecho cuando:** hay pruebas de API de retirar (solo el origen y solo si está pendiente) y un
  checklist manual.

#### T4.06 · Reingreso

- **Brecha:** FUN-08 · **Agente:** `backend-datos` y `bandeja-blazor` · **Depende de:** T3.09, T3.12
- **Modificar**
  - `IPostulacionService` y su implementación: `MarcarReingresoAsync(int postulacionId, int analistaId)`.
  - `Api/Controllers/PostulacionesController.cs`: `POST {id}/reingreso`, con el mismo control de
    acceso que `etapa`.
  - `R01Asignacion.cs`: sin contexto, con una sola postulación viva y `OrigenEleccion == Ninguna`,
    `TomarContextoDePostulacion`.
  - `R16Archivado.cs`: excluir `Reingreso`.
  - `Reporting/ReportingReadModel.cs`: `Reingreso` suma a `EnProceso`.
  - `Frontend/Servicios/ClienteApi.cs`, `Components/Pages/Tablero.razor` (acción en la tarjeta) y
    `Components/Bandeja/PanelChat.razor` (acción en el chip).
- **Crear:** escenario: se marca reingreso a un descartado, escribe tras 10 días y va directo a su
  analista sin menú.
- **Hecho cuando:** pasa.

#### T4.07 · Desambiguación multi-cuenta (entrada)

- **Brecha:** FUN-09 · **Agente:** `reglas-negocio` · **Depende de:** T3.09, T3.11
- **Crear:** `Application/Reglas/Implementaciones/R06Desambiguacion.cs` (prioridad 17).
- **Modificar**
  - `EjecutorAcciones.cs` (`MostrarMenuProcesos`): botones `proc_{id}` con título «{Cuenta}: {Vacante}»
    recortado, más «Otra empresa». Si hay más de 3 opciones, lista.
  - `R01Asignacion.cs`: `PostulacionElegidaId` da `TomarContextoDePostulacion`.
  - `FabricaContextoRegla.cs`: botón `otra_empresa` da `OrigenEleccion.Ninguna` con la marca
    `PidioOtraEmpresa` (agregarla al contexto).
  - `R09RepreguntaEmpresa.cs`: con `PidioOtraEmpresa`, `LimpiarCuentaContexto` y
    `MostrarMenuEmpresas`.
  - `RegistroDependencias.cs` y `EntornoDeReglas`.
- **Crear:** `tests/.../Escenarios/E19MultiCuentaTests.cs` (parte de entrada).
- **Hecho cuando:** pasa.

#### T4.08 · Prefijo multi-cuenta en la respuesta del analista

- **Brecha:** FUN-09 · **Agente:** `reglas-negocio` · **Depende de:** T4.07, T1.11
- **Crear:** `Application/Reglas/PrefijoMultiCuenta.cs`, con
  `static string Aplicar(string texto, IReadOnlyList<PostulacionVigente> postulaciones, int? cuentaContextoId)`.
- **Modificar:** `Application/Casos/EnvioAnalista.cs`: en texto libre, aplicar el prefijo con el
  contexto de `ParaEnvioSalienteAsync`. La vista previa en la bandeja es opcional y no se incluye.
- **Crear:** `E19` (parte de salida), más pruebas unitarias del helper.
- **Hecho cuando:** pasa.

#### T4.09 · Cierre de cortesía controlado

- **Brecha:** FUN-10 · **Agente:** `reglas-negocio` y `backend-datos` · **Depende de:** T3.12, T3.01
- **Modificar**
  - `Contracts/Bandeja/PeticionesBandeja.cs`: `PeticionMarcar(..., bool EnviarCierre = true)` y
    `PeticionMoverEtapa(int EtapaId, bool EnviarCierre = true)`.
  - Controladores `ConversacionesController.Marcar` y `PostulacionesController.MoverEtapa`: pasar
    el valor.
  - `Application/Casos/AccionesBandeja.cs`: parámetro `enviarCierre`, que viaja en el payload del
    evento.
  - `IPostulacionService` y su implementación:
    `MarcarCierrePendienteAsync(int postulacionId, bool enviar, bool automatico)`, que no hace nada
    si ya tiene `FechaCierreCortesia`, y `ListarCierresPendientesAsync(int maximo)`.
  - `Application/Casos/ProcesadorOutbox.cs`: payload con `EnviarCierre`, llamando al servicio
    **dentro** de la transacción antes de evaluar.
  - `Application/Reglas/Implementaciones/R12CierreCortesia.cs`: reescrita según `03` §FUN-10.
  - `Application/Casos/BarridoTiempo.cs`: `ProcesarPostulacionAsync(int postulacionId)`.
  - `Worker/ServicioBarridoTiempo.cs`: candidatas por postulación.
- **Crear:** `tests/.../Escenarios/E14KanbanTests.cs` (cierre único) y `E15CierreFueraHorarioTests.cs`.
- **Hecho cuando:** E14 y E15 pasan.

#### T4.10 · Cierre de cortesía en la bandeja

- **Brecha:** FUN-10 · **Agente:** `bandeja-blazor` · **Depende de:** T4.09
- **Modificar**
  - `Frontend/Components/Bandeja/AccionesRapidas.razor`: casilla «Enviar mensaje de cierre», marcada
    por defecto, en «Descartar».
  - `Frontend/Components/Pages/Tablero.razor`: al soltar en Descartado, confirmación con la misma
    casilla.
  - `Frontend/Servicios/ClienteApi.cs`.
- **Hecho cuando:** hay un checklist manual.

#### T4.11 · Archivado por postulación con aviso previo

- **Brecha:** FUN-11, COR-14 (M6) · **Agente:** `reglas-negocio` · **Depende de:** T4.06, T4.09
- **Modificar**
  - `Application/Reglas/Implementaciones/R16Archivado.cs`: por postulación (disparador
    `TiempoTranscurrido` con `Postulacion` presente), según `03` §FUN-11. Borrar el comentario
    obsoleto.
  - `IPostulacionService` y su implementación: `ListarPorArchivarAsync(int dias, int maximo)` y
    `ListarPorAvisarArchivadoAsync(int dias, int diasAviso, int maximo)`.
  - `Worker/ServicioBarridoTiempo.cs`: candidatas por postulación.
  - `tests/.../Casos/R16ArchivadoTests.cs`: rehacer.
- **Hecho cuando:** `EstadoPostulacion.Archivada` se asigna en las pruebas.

#### T4.12 · Archivado de la conversación y reactivación

- **Brecha:** FUN-11 (AL10) · **Agente:** `reglas-negocio` · **Depende de:** T4.11
- **Crear:** `Application/Reglas/Implementaciones/R16ArchivadoConversacion.cs` y
  `R16Reactivacion.cs` (prioridad 12).
- **Modificar:** `RegistroDependencias.cs`, `EntornoDeReglas` y `ServicioBarridoTiempo.cs`.
- **Crear:** `tests/.../Escenarios/E13ArchivadoTests.cs`: aviso a los 83 días; archivado a los 90;
  vuelve a escribir, `EnMenuBot` y menú. Otra variante con `Reingreso` vuelve `Activa` con su
  analista.
- **Hecho cuando:** E13 pasa.

#### T4.13 · Aviso de retorno de ausencia

- **Brecha:** FUN-12 · **Agente:** `backend-datos` · **Depende de:** T2.09
- **Crear:** `Application/Casos/AvisoRetornoAusencia.cs`.
- **Modificar**
  - `IAusenciaService` y su implementación: `ListarFinalizadasSinAvisoAsync(DateTime ahora)` y
    `MarcarAvisoRetornoAsync(int ausenciaId)`.
  - `IAuditoriaService` y su implementación: `ContarAsync(string accion, DateTime desde, DateTime hasta, IReadOnlyCollection<int> cuentaIds)`,
    o una consulta equivalente en `ConversacionService`.
  - `Worker/ServicioBarridoTiempo.cs` y `RegistroDependencias.cs`.
- **Hecho cuando:** hay una prueba con ausencia terminada: un aviso al titular con el conteo, que
  no se repite.

---

### BLOQUE 5 — Operación y datos

#### T5.01 · Estado de entrega visible y aviso de fallo

- **Brecha:** FUN-13 (M2) · **Agente:** `integracion-whatsapp` y `bandeja-blazor` · **Depende de:** T1.07
- **Modificar**
  - `IMensajeService.ActualizarEstadoEntregaAsync` devuelve `Task<ResultadoAcuse?>`; record en
    Domain.
  - `Application/Casos/RecepcionWebhook.cs`: si `PasoAFallido`, publica `AnalistaNotificado` en la
    misma transacción del acuse.
  - `Contracts/Bandeja/ContratosBandeja.cs` (`MensajeResumen`): suma `string? Error`.
  - `Api/Mapeo/MapeoBandeja.cs`.
  - `Frontend/Components/Bandeja/PanelChat.razor` y `wwwroot/bandeja.css`: íconos por estado y
    tooltip con el error.
- **Crear:** `tests/.../Escenarios/E22AcuseFallidoTests.cs`.
- **Hecho cuando:** E22 pasa.

#### T5.02 · Adjuntos: modelo, migración e intérprete

- **Brecha:** ARQ-10 (M3) · **Agente:** `integracion-whatsapp` (V33) · **Depende de:** T1.03
- **Modificar**
  - `Domain/Entidades/Conversaciones.cs`: entidad `MensajeAdjunto` y `Mensaje.Adjuntos`.
  - `Domain/Enums/Enumeraciones.cs`: `EstadoAdjunto`.
  - `Domain/Interfaces/IWhatsAppProvider.cs`: `MedioEntranteDto` y `MensajeEntranteDto.Medio`.
  - `Infrastructure/Persistencia/Configuraciones.cs` y `RrhhDbContext.cs`.
  - `Infrastructure/Proveedores/PayloadsWebhook.cs` e `InterpreteWebhookMeta.cs`: leer image,
    document, audio, video y sticker (id, mime_type, filename, caption).
  - `IMensajeService` y su implementación: `RegistrarAdjuntoAsync(long mensajeId, MedioEntranteDto medio)`.
  - `Application/Casos/RecepcionWebhook.cs`: dentro de la transacción del entrante.
  - `tests/.../Proveedores/PayloadsDePrueba.cs` e `InterpreteWebhookMetaTests.cs`.
- **Migración:** `AdjuntosEntrantes`.
- **Hecho cuando:** un payload de documento da un adjunto `Pendiente` con nombre y mime.

#### T5.03 · Adjuntos: descarga desde el proveedor

- **Brecha:** ARQ-10 · **Agente:** `integracion-whatsapp` · **Depende de:** T5.02
- **Modificar**
  - `Domain/Interfaces/IWhatsAppProvider.cs`:
    `Task<MedioDescargado?> DescargarMedioAsync(string proveedorMedioId, CancellationToken ct)`,
    con `record MedioDescargado(Stream Contenido, string MimeType, long? Tamano)`.
  - `MetaCloudProvider.cs`: `GET {Version}/{mediaId}` y luego `GET url` con Bearer (sin límite de
    envío).
  - `Dialog360Provider.cs`: `GET media/{id}`.
  - `ProveedorSimulado.cs`: bytes fijos.
- **Hecho cuando:** hay pruebas con un handler falso.

#### T5.04 · Adjuntos: almacenamiento, escaneo y Worker

- **Brecha:** ARQ-10 · **Agente:** `jobforms-datos` · **Depende de:** T5.03
- **Crear**
  - `Infrastructure/Almacenamiento/AlmacenamientoArchivosLocal.cs`: base extraída de
    `AlmacenamientoCvLocal` (cuarentena, escaneo, tope, borrado de parciales).
  - `Infrastructure/Almacenamiento/AlmacenamientoAdjuntosLocal.cs` y `OpcionesAdjuntos.cs`
    (`Carpeta`, `ExtensionesPermitidas`, `TamanoMaximoMb`).
  - `Application/Casos/DescargaAdjuntos.cs` y `Worker/ServicioDescargaAdjuntos.cs`.
- **Modificar**
  - `AlmacenamientoCvLocal.cs`: heredar de la base.
  - `Domain/Interfaces/ServiciosDominio.cs`: `IAlmacenamientoAdjuntos`.
  - `RegistroDependencias.cs`, `Worker/Program.cs`, `OpcionesWorker.cs`, `Latido.cs`,
    `ChequeoWorker.cs`, `appsettings.json` de Api y Worker.
- **Hecho cuando:** `EscaneoCvTests` sigue verde y hay pruebas nuevas de descarga
  limpia / amenaza / tope.

#### T5.05 · Adjuntos: ver en la bandeja y purga

- **Brecha:** FUN-14 · **Agente:** `bandeja-blazor` · **Depende de:** T5.04
- **Modificar**
  - `Contracts/Bandeja/ContratosBandeja.cs`: `AdjuntoResumen` y `MensajeResumen.Adjuntos`.
  - `Api/Mapeo/MapeoBandeja.cs`.
  - `MensajeService.ListarPorConversacionAsync`: `Include(Adjuntos)`.
  - `Api/Controllers/ConversacionesController.cs`: `GET {id:int}/adjuntos/{adjuntoId:long}`.
  - `Frontend/Program.cs`: endpoint `GET /descargas/adjuntos/{conversacionId}/{adjuntoId}` que usa
    la sesión del circuito. **Consultar a `bandeja-blazor` la forma de pasar el token en Blazor
    Server;** una alternativa aceptable es un token de descarga de un solo uso emitido por la Api.
  - `Frontend/Components/Bandeja/PanelChat.razor`.
  - `Worker/ServicioPurgaCv.cs`: purga de adjuntos por `datos.retencion_adjuntos_dias`.
- **Crear:** `tests/.../Escenarios/E23AdjuntoTests.cs`.
- **Hecho cuando:** E23 pasa y hay un checklist manual.

#### T5.06 · Outbox: purga de procesados

- **Brecha:** ARQ-13 · **Agente:** `backend-datos` · **Depende de:** T2.12
- **Modificar**
  - `IEventoSistemaService` y su implementación: `PurgarProcesadosAsync(int dias, int maximo)`,
    borrando en lotes con `ExecuteDeleteAsync`.
  - `Worker/ServicioPurgaCv.cs`: renombrar la clase y el archivo a `ServicioMantenimientoDatos`,
    manteniendo el nombre del latido `PurgaCv` para no romper el monitoreo (documentarlo). Sumar la
    purga de outbox.
  - `ConversacionService.TransferirAsync` y `ResponderTransferenciaAsync`: los avisos no incluyen
    nombre ni teléfono del postulante.
- **Hecho cuando:** hay una prueba de purga de eventos procesados viejos.

#### T5.07 · Anonimización extendida y retención por última actividad

- **Brecha:** FUN-16 (AL6), A5 · **Agente:** `jobforms-datos` · **Depende de:** T5.06, T5.05, T1.01
- **Modificar**
  - `Infrastructure/Servicios/PostulanteService.cs` (`AnonimizarDatosAsync`): según `03` §FUN-16,
    dentro de `IUnidadTrabajo`.
  - `JobFormsService.ListarCvsPorPurgarAsync`: según `03` §FUN-16.
  - `IMensajeService`, `IEventoSistemaService`, `IAuditoriaService`: los métodos de anonimización
    por conversación.
- **Crear:** `tests/.../Escenarios/E20AnonimizacionTests.cs`, más casos en `RetencionDatosTests.cs`
  (no purga con un proceso vivo).
- **Hecho cuando:** E20 pasa.

#### T5.08 · Panel de alertas operativas

- **Brecha:** FUN-15 · **Agente:** `bandeja-blazor` · **Depende de:** T1.14
- **Crear:** `Api/Controllers/OperacionController.cs` y `Contracts/Administracion/AlertaOperativaResumen`.
- **Modificar**
  - `Frontend/Servicios/ClienteApi.cs`.
  - `Frontend/Components/Pages/Configuracion.razor`: sección de alertas.
  - `Frontend/Components/Layout/NavMenu.razor`: contador.
  - `tests/.../Infraestructura/AdministracionPorRolTests.cs`: la escritura de «resolver» queda con
    dueño declarado.
- **Hecho cuando:** `AutorizacionTests` pasa y hay un checklist manual.

#### T5.09 · Versión de seguridad en el token

- **Brecha:** ARQ-11 (M10) · **Agente:** `backend-datos` (V34) · **Depende de:** T0.02
- **Modificar**
  - `Domain/Entidades/Organizacion.cs`: `Analista.VersionSeguridad`.
  - `Configuraciones.cs`: `HasDefaultValue(1)`.
  - `IAnalistaService` y su implementación: `ObtenerEstadoSeguridadAsync(int id)` devuelve
    `(bool Activo, int Version)`.
  - `AutenticacionService.EstablecerContrasenaAsync` y `ResultadoAutenticacion`: incluir la
    versión e incrementarla.
  - `Api/Seguridad/EmisorTokens.cs`: claim `ver`.
  - `Api/Program.cs`: `OnTokenValidated` con `IMemoryCache` (60 s).
- **Migración:** `VersionSeguridadAnalista`.
- **Hecho cuando:** `AutenticacionTests` confirma que un token viejo tras restablecer la contraseña
  da 401 (después de invalidar la caché).

#### T5.10 · Cerrar sesiones de un analista

- **Brecha:** FUN-18 · **Agente:** `backend-datos` · **Depende de:** T5.09
- **Modificar:** `Api/Controllers/SesionController.cs`: `POST analistas/{id}/cerrar-sesiones`, rol
  Sistemas, que incrementa la versión y hace `cache.Remove($"seg:{id}")`. Implementación en
  `AnalistaService`.
- **Hecho cuando:** hay una prueba de API.

#### T5.11 · Baja y edición de analistas

- **Brecha:** FUN-19 · **Agente:** `backend-datos` y `bandeja-blazor` · **Depende de:** T5.09, T1.13
- **Modificar**
  - `IAnalistaService` y su implementación: `ActualizarAsync(...)` y `ObtenerCarteraAsync(int id)`,
    según `03` §FUN-19.
  - `ConversacionService`: `ReasignarCarteraAsync(int analistaId, int autorId)`.
  - `Contracts/Bandeja/PeticionesBandeja.cs`: `PeticionEditarAnalista`, `CarteraAnalista`.
  - `Api/Controllers/AnalistasController.cs`: `PATCH {id}` (política `Estructura`) y
    `GET {id}/cartera` (`Jefatura`).
  - `Frontend/Components/Pages/Equipo.razor` y `ClienteApi.cs`.
- **Crear:** `tests/.../Escenarios/E21BajaAnalistaTests.cs`.
- **Hecho cuando:** E21 pasa.

#### T5.12 · Edición de cuentas y vacantes

- **Brecha:** FUN-20 · **Agente:** `backend-datos` y `bandeja-blazor` · **Depende de:** T3.08, T1.13
- **Modificar**
  - `ICuentaService` y su implementación: `ActualizarVacanteAsync`, `ReabrirVacanteAsync` y
    `ActualizarCuentaAsync`, con alertas según `03` §FUN-20.
  - `Contracts`: `PeticionEditarVacante`, `PeticionEditarCuenta`.
  - `Api/Controllers/VacantesController.cs`: `PATCH {id}` y `PATCH {id}/reabrir`.
  - `Api/Controllers/CuentasController.cs`: `PATCH {id}` (política `Estructura`).
  - `Frontend/Components/Pages/Vacantes.razor`, `Equipo.razor` y `ClienteApi.cs`.
  - `tests/.../Infraestructura/AdministracionPorRolTests.cs`.
- **Hecho cuando:** hay pruebas de permisos y validación (código duplicado da 422).

#### T5.13 · Métricas por tanda en horas hábiles

- **Brecha:** FUN-17 (M7) · **Agente:** `reporting-metricas` · **Depende de:** T2.02
- **Modificar**
  - `Reporting/ReportingReadModel.cs`: tandas según `03` §FUN-17, con `CalendarioLaboral` y los
    tramos de `ReportingDbContext`.
  - `Contracts/Metricas/ContratosMetricas.cs`.
  - `Frontend/Components/Pages/Metricas.razor`.
  - `tests/.../Reporting/MetricasGerenciaTests.cs` y `EntornoDeMetricas.cs`.
- **Hecho cuando:** hay una prueba con dos tandas en un hilo (dos mediciones) y un mensaje de viernes
  a las 17:50 respondido el lunes a las 09:10 (20 minutos hábiles).

---

### BLOQUE 6 — Cierre

#### T6.01 · Higiene y literales

- **Brecha:** COR-17, COR-18 · **Agente:** `reglas-negocio` y `backend-datos` · **Depende de:**
  todos los de B3–B5
- **Pasos**
  1. Buscar literales de horas y días en `Application/Reglas`: solo deben quedar límites de Meta
     comentados.
  2. `CuentaService.CrearVacanteAsync`: auditoría con el `HcId` real.
  3. `AutenticacionService.HashFalso` como `static readonly`.
  4. Buscar `DateTime.UtcNow` en `src/` (§3.7).
- **Hecho cuando:** las búsquedas quedan limpias.

#### T6.02 · Documentación final

- **Brecha:** COR-19 · **Agente:** `arquitecto` y `despliegue-operacion` · **Depende de:** T6.01
- **Modificar**
  - `README.md`: tabla «Estado» real; despachador, alertas, adjuntos y tomar; parámetros nuevos;
    `WhatsApp:NumeroPublico`, `JobForms:*`, `Worker:*`; checklist de producción. Agregar: número
    público definido; Jefatura con usuario; plantillas nuevas si se registran; monitoreo del
    chequeo de alertas.
  - `CLAUDE.md`: los envíos del bot pasan por la cola, P1, y la tabla de prioridades de reglas
    actualizada.
  - `docs/decisiones.md`: cerrar los «pendientes» resueltos (V1 multi-cuenta, V21 vencimiento, riesgo
    «reingreso»).
- **Hecho cuando:** hay revisión cruzada de afirmaciones del README contra el código, sin 🔀
  pendientes.

#### T6.03 · Verificación integral

- **Brecha:** §4 de `03` · **Agente:** `despliegue-operacion` · **Depende de:** T6.02
- **Pasos**
  1. `dotnet build --nologo` y `dotnet test --nologo`.
  2. Con `RRHH_PRUEBAS_SQL`: `dotnet test --filter "FullyQualifiedName~SqlServer"`.
  3. Confirmar que E01–E23 existen y pasan.
  4. `dotnet ef database update` en una base local limpia y en una copia con datos, para ver que la
     migración de datos de T2.04 funciona.
  5. Recorrido manual con `scripts/probar-webhook-local.ps1` y `probar-jobforms-local.ps1`, según el
     guion del README.
- **Hecho cuando:** todo verde, con el reporte en §6.

---

## 6. Registro de avance

> **Instrucciones:** marcar `[x]` solo con build y pruebas en verde. En «Notas» van la fecha, el hash
> del commit (si lo hubo) y cualquier desvío respecto de la especificación. Las tareas nuevas se
> agregan con sufijo (`T3.04a`).

| ✔ | Tarea | Depende de | Notas |
|---|---|---|---|
| [x] | T0.01 Línea base | — | 2026-09-14 · `d579e29` · Build 0/0, pruebas 349 ok / 4 omitidas. **Desvío:** commit directo en `master`, sin rama `auditoria/linea-base` |
| [x] | T0.02 Decisiones V28–V36 y D7; puntero en CLAUDE.md | T0.01 | 2026-09-14 · Ratificadas todas sin objeciones. Se agregó también la fila D6 (JWT, citada en V20 y ausente de la tabla) y referencias cruzadas en V8, V14, V20 y V22. Solo documentación |
| [x] | T0.03 Proveedores sin reintentos implícitos | T0.01 | 2026-09-15 · Build 0/0, pruebas 360 ok / 4 omitidas (+11). Clasificación compartida en `ClasificadorFallosHttp`: `HttpRequestError` DNS/conexión/TLS → Transitorio; resto → Ambiguo. Además de lo pedido: `Dialog360ProviderTests` y `ComposicionProveedorTests` (el contenedor real no reintenta un 503; antes hacía 4 peticiones). README actualizado |
| [x] | T0.04 Limitador con parámetro | T0.01 | 2026-09-15 · Build 0/0, pruebas 372 ok / 4 omitidas (+12). **Desvíos:** `LimitadorEnvio` recibe `Func<CancellationToken, ValueTask<int>>` en vez de `Func<int>` (evita sync-over-async); `ProveedorParametrosEnvio` usa `TimeProvider` (registrado con `TryAddSingleton`, T0.08 lo formaliza). Se quitó `LimitadorEnvio.MaximoPorSegundo`. Latencia real de un cambio: ≤30 s en la Api, ≤60 s en el Worker (se suma la caché de `ConfiguracionReglasService`). La descripción «por proceso emisor» del parámetro (COR-12) queda para T2.10 |
| [x] | T0.05 Plantillas inactivas por defecto | T0.01 | 2026-09-15 · Build 0/0, pruebas 374 ok / 4 omitidas (+2). `has-pending-model-changes`: sin cambios en el snapshot. Prueba extra: ninguna plantilla sembrada en el modelo de diseño está activa. Hecha sin subagente por su tamaño |
| [x] | T0.06 Límite de velocidad del webhook de Google y 429 | T0.01 | 2026-09-15 · Build 0/0, pruebas 384 ok / 4 omitidas (+10). **Desvío (manda 03 §COR-15):** partición por secreto válido (`apps-script`, 600/min); sin secreto, por IP con prefijo propio y el límite público. Registro extraído a `Api/Configuracion/LimitesVelocidad.cs`; comparación del secreto compartida en `Api/Seguridad/SecretoJobForms.cs`. **Agregado:** `OnRejected` emite `Retry-After`, que el script lee (el middleware no lo pone solo). Script: 5 reintentos, 429 reintentable, espera tope 60 s. **Hay que volver a pegar `Codigo.gs` en el proyecto de Apps Script** |
| [ ] | T0.07 Validación del JobForms público | T0.06 | |
| [ ] | T0.08 TimeProvider en Infrastructure | T0.01 | |
| [ ] | T0.09 TimeProvider en Application y Api | T0.08 | |
| [ ] | T0.10 Reloj simulado en pruebas | T0.09 | |
| [ ] | T1.01 Unidad de trabajo | T0.10, T0.02 | |
| [ ] | T1.02 Pruebas SQL y arnés de escenarios | T1.01 | |
| [ ] | T1.03 Webhook atómico (E17) | T1.02 | |
| [ ] | T1.04 JobForms atómico | T1.01 | |
| [ ] | T1.05 Cola de envíos: dominio | T1.01 | |
| [ ] | T1.06 Cola de envíos: migración `ColaDeEnvios` | T1.05 | |
| [ ] | T1.07 Cola de envíos: servicio | T1.06 | **Pendiente (V29):** la Api también escribe en la cola; `TomarLoteEnColaAsync` no debe poder tomar la fila de una respuesta del analista (ver T1.11) |
| [ ] | T1.08 Validador y despacho | T1.07, T0.03 | |
| [ ] | T1.09 Worker de despacho | T1.08 | |
| [ ] | T1.10 El ejecutor encola | T1.09 | **Pendiente:** la clave del barrido difiere entre la especificación de 03 (ARQ-03: `barrido:{ConversacionId}:{regla}:{marca}`) y la de 04 (`barrido:{id}:{yyyyMMddHHmm}`); también `{EventoId}:{i}` frente a `evt:`. Manda la especificación de 03 salvo decisión en contra |
| [ ] | T1.11 Respuesta del analista idempotente | T1.10 | **Ajuste (V29):** la fila del analista nace directamente `Enviando`, no `EnCola`, para que el despachador no la tome entre ambos pasos. Alternativa: `UPDATE` condicionado al estado en los dos caminos |
| [ ] | T1.12 Consumidor transaccional y motor estricto (E16) | T1.10 | Corregir también el comentario de `MotorReglas.cs:34-35`, que hoy ya es falso (el evento se marca procesado, no queda para reintento) |
| [ ] | T1.13 Alertas: modelo, migración `AlertasOperativas` | T1.01 | |
| [ ] | T1.14 Alertas en lugar de eventos huérfanos | T1.13, T1.10 | |
| [ ] | T2.01 CalendarioLaboral | T0.02 | |
| [ ] | T2.02 Fachada de horario | T2.01 | |
| [ ] | T2.03 Estados de conversación: dominio | T0.02 | |
| [ ] | T2.04 Migración `SeguimientoConversacion` | T2.03 | |
| [ ] | T2.05 Estados: servicio, acceso y bandeja | T2.04 | |
| [ ] | T2.06 Migración `DesenlaceYCierre` | T0.02 | |
| [ ] | T2.07 Migración `CodigoAvisoVacante` | T0.02 | |
| [ ] | T2.08 Migración `VencimientoTransferencias` | T0.02 | |
| [ ] | T2.09 Migración `IndicesYRetorno` | T0.02 | |
| [ ] | T2.10 Migración `ParametrosAtencionPreferente` | T0.02 | Incluir el `UpdateData` de la descripción de `envio.maximo_por_segundo`: «por proceso emisor» (COR-12, pendiente de T0.04) |
| [ ] | T2.11 Contexto y acciones: dominio | T2.03, T2.06, T2.08, T1.10 | |
| [ ] | T2.12 Payload mínimo con instantánea | T2.11, T1.03 | |
| [ ] | T2.13 Fábrica con contexto nuevo | T2.12, T2.02, T2.07 | |
| [ ] | T2.14 Ejecutor con acciones nuevas | T2.13, T1.14 | |
| [ ] | T3.01 P1: texto libre del bot (E04 parte 1) | T2.14 | |
| [ ] | T3.02 R3 reescrita (E01) | T3.01 | |
| [ ] | T3.03 Menú con opciones válidas | T1.14 | |
| [ ] | T3.04 R9 enlace sin repetición (E04) | T2.13, T3.03 | |
| [ ] | T3.05 R19 con contador (E05 parte 1) | T2.14, T2.10 | |
| [ ] | T3.06 Menú paginado (E03) | T3.05 | |
| [ ] | T3.07 Código de aviso y reconocimiento (E02) | T3.06, T2.07 | |
| [ ] | T3.08 Enlace de aviso en administración | T3.07 | Requiere `WhatsApp:NumeroPublico` |
| [ ] | T3.09 R9 repregunta con continuidad (E11, E12) | T2.13, T3.05 | |
| [ ] | T3.10 R14 respeta lo asignado (E10) | T2.14 | |
| [ ] | T3.11 Aviso multi-cuenta único | T3.10 | |
| [ ] | T3.12 Kanban reversible | T2.06 | |
| [ ] | T3.13 Tomar: servicio | T2.05, T1.01 | |
| [ ] | T3.14 Tomar: Api y bandeja (E07) | T3.13 | |
| [ ] | T3.15 Escalamiento sin carrera | T2.14 | |
| [ ] | T4.01 Segundo nivel (E08) | T3.15, T2.13 | |
| [ ] | T4.02 Marca «vencida» | T4.01 | |
| [ ] | T4.03 Derivación por silencio y plazo (E05, E06) | T3.05, T2.13 | |
| [ ] | T4.04 Vencimiento de transferencias (E09) | T2.08, T2.13 | |
| [ ] | T4.05 Retiro de transferencias y bandeja | T4.04 | |
| [ ] | T4.06 Reingreso | T3.09, T3.12 | |
| [ ] | T4.07 Desambiguación multi-cuenta (E19 entrada) | T3.09, T3.11 | |
| [ ] | T4.08 Prefijo multi-cuenta (E19 salida) | T4.07, T1.11 | |
| [ ] | T4.09 Cierre controlado (E14, E15) | T3.12, T3.01 | |
| [ ] | T4.10 Cierre en la bandeja | T4.09 | |
| [ ] | T4.11 Archivado por postulación | T4.06, T4.09 | |
| [ ] | T4.12 Archivado de conversación y reactivación (E13) | T4.11 | |
| [ ] | T4.13 Aviso de retorno de ausencia | T2.09 | |
| [ ] | T5.01 Estado de entrega (E22) | T1.07 | |
| [ ] | T5.02 Adjuntos: modelo e intérprete | T1.03 | |
| [ ] | T5.03 Adjuntos: descarga del proveedor | T5.02 | |
| [ ] | T5.04 Adjuntos: almacenamiento y Worker | T5.03 | **Pendiente (V33):** fijar desde cuándo cuenta `datos.retencion_adjuntos_dias`; V33 recomienda seguir A5 (última actividad del postulante), no la fecha de recepción |
| [ ] | T5.05 Adjuntos: bandeja y purga (E23) | T5.04 | |
| [ ] | T5.06 Purga de outbox | T2.12 | |
| [ ] | T5.07 Anonimización extendida (E20) | T5.06, T5.05, T1.01 | |
| [ ] | T5.08 Panel de alertas | T1.14 | |
| [ ] | T5.09 Versión de seguridad | T0.02 | ARQ-11 en 03 depende de FUN-19 (T5.11); aquí solo de T0.02. Revisar el orden al llegar |
| [ ] | T5.10 Cerrar sesiones | T5.09 | |
| [ ] | T5.11 Baja y edición de analistas (E21) | T5.09, T1.13 | |
| [ ] | T5.12 Edición de cuentas y vacantes | T3.08, T1.13 | |
| [ ] | T5.13 Métricas por tanda | T2.02 | |
| [ ] | T6.01 Higiene y literales | B3–B5 | |
| [ ] | T6.02 Documentación final | T6.01 | |
| [ ] | T6.03 Verificación integral | T6.02 | |

### Cobertura de escenarios

| E | Tarea |
|---|---|
| E01 | T3.02 |
| E02 | T3.07 |
| E03 | T3.06 |
| E04 | T3.01, T3.04 |
| E05 | T3.05, T4.03 |
| E06 | T4.03 |
| E07 | T3.14 |
| E08 | T4.01 |
| E09 | T4.04 |
| E10 | T3.10 |
| E11–E12 | T3.09 |
| E13 | T4.12 |
| E14 | T3.12, T4.09 |
| E15 | T4.09 |
| E16 | T1.12 |
| E17 | T1.03 |
| E18 | T0.03 |
| E19 | T4.07, T4.08 |
| E20 | T5.07 |
| E21 | T5.11 |
| E22 | T5.01 |
| E23 | T5.05 |

### Trabajo en paralelo seguro

Si hay más de una sesión, se pueden llevar a la vez:

- **Dentro del Bloque 0:** T0.03–T0.07 son independientes entre sí.
- **Bloque 2, después de T0.02:** las migraciones T2.06, T2.07, T2.08, T2.09 y T2.10 son
  independientes. **Generarlas de a una y en orden** para no chocar en el snapshot de EF.
- **B5 junto con B3/B4:** T5.09–T5.10, T5.13 y T5.01 no tocan reglas.
