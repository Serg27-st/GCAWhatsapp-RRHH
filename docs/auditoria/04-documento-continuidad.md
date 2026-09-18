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
| Worker | `Worker/Program.cs`, `ConsumidorOutbox`, `ServicioBarridoTiempo`, `ServicioMantenimientoDatos`, `ServicioReintentoEnvios`, `ServicioDescargaAdjuntos`, `GuardiaInstancia`, `Latido.cs`, `OpcionesWorker.cs` |
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
| [x] | T0.07 Validación del JobForms público | T0.06 | 2026-09-15 · Build 0/0, pruebas 421 ok / 4 omitidas (+37). `DocumentoIdentidad.TryNormalizar` (quita espacios, guiones y puntos); `ValidadorCvUrl` con host exacto; `DominiosCvPermitidos` nace nulo y se lee `DominiosCvPermitidosEfectivos` (el binder suma índices sobre arrays con datos). En `Enviar`, una invitación completada no guarda CV y responde `yaRecibido` (V27); el CV se borra si la recepción lanza o devuelve `YaRecibido`. `DocumentoIdentidad` se escribió antes que su prueba; el resto, prueba primero |
| [x] | T0.07a DNI normalizado en buscador, historial y anonimización | T0.07 | 2026-09-15 · Build 0/0, pruebas 429 ok / 4 omitidas (+6 en `DniNormalizadoEnBandejaTests`, prueba primero). DNI sin formato → 400 sin consultar. Además: `PostulanteService.AnonimizarDatosAsync` ya no pone el DNI en el mensaje de la excepción, que iba al log y a la respuesta (Regla 17). DNIs guardados antes de T0.07 solo con `Trim()` no se migran: sin datos de producción todavía. Hecha sin subagente. **Origen:** `ConversacionesController.Buscar` (~l.59), `PostulantesController.Historial` (~l.27) y `DELETE /postulantes/{dni}` (~l.76) comparan el DNI crudo; lo guardado por JobForms ya sale normalizado. Aplicar `DocumentoIdentidad.TryNormalizar`. Agente: `jobforms-datos` |
| [x] | T0.07b Apps Script: documento alfanumérico | T0.07 | 2026-09-15 · +2 pruebas de contrato. El script manda el documento tal como se escribió (`texto(...)`) y la Api normaliza; se eliminó `soloDigitos`. **Hay que volver a pegar `Codigo.gs` en Google** (junto con el cambio de T0.06). Hecha sin subagente. **Origen:** `Codigo.gs:96` usa `soloDigitos()`, que borra las letras de un carné de extranjería; con T0.07 llega mutilado y la Api lo rechaza con 422. Reemplazar por una limpieza que conserve letras. Requiere volver a pegar el script en Google. Agente: `jobforms-datos` |
| [x] | T0.08 TimeProvider en Infrastructure | T0.01 | 2026-09-15 · Build 0/0, pruebas 430 ok / 4 omitidas (+1 de composición: un reloj registrado antes de `AgregarInfraestructura` es el que usan los servicios). 48 lecturas reemplazadas en 16 servicios más los proveedores; grep sin resultados en Infrastructure. `TryAddSingleton(TimeProvider.System)` al inicio de `AgregarInfraestructura`. `InterpreteWebhookMeta` (estático) recibe `ahoraUtc`. Un solo instante por operación en 4 métodos. Tests con `TimeProvider.System` hasta T0.10. Sin cambios de modelo EF |
| [x] | T0.09 TimeProvider en Application y Api | T0.08 | 2026-09-15 · Build 0/0, pruebas 433 ok / 4 omitidas (+3 en `MapeoBandejaTests`: borde de la ventana de 24 h, prueba primero). `MapeoBandeja.AResumen(Conversacion, DateTime ahoraUtc, ...)`; `ConversacionesController` lee el reloj una vez por petición. Reloj inyectado también en `AnalistasController`, `ReportesController` y `EmisorTokens` (solo el vencimiento emitido; la validación JWT sigue con el reloj del sistema). Grep en `src/`: solo `ChequeoWorker` y el Frontend, como admite ARQ-01. `RecepcionJobForms` no leía el reloj. Hecha sin subagente. **Nota de T0.08:** `Application/Casos/EnvioAnalista.cs`, `ReintentoEnvios.cs`, `Api/Mapeo/MapeoBandeja.cs` (previstos), y **también** `Api/Controllers/AnalistasController.cs` (2), `ReportesController.cs` (1) y `Api/Seguridad/EmisorTokens.cs` (1), que la tarea no lista. `Api/Salud/ChequeoWorker.cs` queda exceptuado por ARQ-01 |
| [x] | T0.10 Reloj simulado en pruebas | T0.09 | 2026-09-15 · Build 0/0, pruebas 435 ok / 4 omitidas (+2 en `RelojSimuladoTests`, prueba primero). `Microsoft.Extensions.TimeProvider.Testing` 10.10.0. `EntornoDeReglas`: `Reloj` (`FakeTimeProvider`, lunes 2026-09-14 10:00 Lima = 15:00 UTC), `Ahora` y `AvanzarAsync`; también la semilla de la vacante usa el reloj. Al conectar el reloj fallaron 7 pruebas que mezclaban `DateTime.UtcNow`: las 23 lecturas en 9 archivos pasaron a `_entorno.Ahora`. Hecha sin subagente. **Cierra el Bloque 0** |
| [x] | T1.01 Unidad de trabajo | T0.10, T0.02 | 2026-09-15 · Build 0/0, pruebas 437 ok / 6 omitidas (+4, prueba primero). `IUnidadTrabajo` (Domain) y `UnidadTrabajoEf` (Persistencia), scoped. Dos pruebas SQL con `[FactConSqlServer]` (rollback real tras un SaveChanges intermedio; reutiliza la transacción abierta sin confirmarla), **verificadas contra `.\SQLEXPRESS`** junto con las 4 SQL existentes: 7/7 ok. `TransactionIgnoredWarning` ignorada en `EntornoDeReglas`; los demás contextos InMemory la agregan cuando adopten la unidad de trabajo. Hecha sin subagente |
| [x] | T1.02 Pruebas SQL y arnés de escenarios | T1.01 | 2026-09-15 · Build 0/0. `SqlServerFixture` (`IAsyncLifetime`, `RRHH_Pruebas_{guid}` con `MigrateAsync`, borrado con `ClearAllPools` + `EnsureDeleted`; sin variable no hace nada) y `SqlServerFixtureTests` (migraciones y semilla), verificadas contra `.\SQLEXPRESS`, sin bases residuales. `Escenarios/PasoConversacion.cs`, `ArnesEscenario.cs` (payloads con wamid único y timestamp del reloj simulado; `Enviados()`, `EstadoConversacion()`, `ConversacionAsync()`, `Notificaciones()`, `Alertas()` —provisionalmente los eventos huérfanos, pasa a `AlertasOperativas` en T1.14—) y `E00HumoTests` (2). `Despachar()` es no-op hasta T1.09 |
| [x] | T1.03 Webhook atómico (E17) | T1.02 | 2026-09-15 · Build 0/0, 439 ok / 9 omitidas; SQL 11/11 contra `.\SQLEXPRESS`. `RecepcionWebhook` recibe `IUnidadTrabajo`: `ObtenerOCrear` fuera; `RegistrarEntrante` + `RegistrarEntrada` + `Publicar` dentro; cada acuse en su transacción. `EntornoDeReglas.Unidad`. E17 escrita antes (no compilaba sin el cambio); no se la vio fallar en ejecución contra el código viejo |
| [x] | T1.04 JobForms atómico | T1.01 | 2026-09-15 · Build 0/0, 439 ok / 10 omitidas; SQL 12/12. `RecepcionJobForms` con `IUnidadTrabajo` desde `RegistrarDesdeFormulario` hasta `Publicar`; `yaRecibido` antes. `RecepcionJobFormsSqlTests` (prueba primero). **Cambio de comportamiento:** un envío rechazado (vacante cerrada, sin consentimiento) ya no deja creado ni actualizado al postulante. Decorador `EventosQueFallanAlPublicar` compartido en `Escenarios/` |
| [x] | T1.05 Cola de envíos: dominio | T1.01 | 2026-09-15 · `TipoSaliente`, `EstadoEntrega.EnCola/Enviando`, `Mensaje.TipoSaliente/OpcionesJson/ClaveIdempotencia`, `SalienteEncolado`, `OpcionesSaliente`. **Desvíos:** `EncolarSalienteAsync(..., bool reservadoParaEnvio)` (la fila del analista nace `Enviando`, resuelve la carrera anotada); `ObtenerPorClaveIdempotenciaAsync`; `RecuperarEnviandoVencidosAsync` devuelve `int`; columna nueva **`FechaTomaEnvio`**: con `FechaEnvio` (hora de encolado) un mensaje que esperó con el Worker detenido parecía vencido apenas tomado y se marcaba ambiguo en pleno envío |
| [x] | T1.06 Cola de envíos: migración `ColaDeEnvios` | T1.05 | 2026-09-15 · Revisada: aditiva, índices con nombre. **Agregado:** `UPDATE Mensajes SET TipoSaliente = 2 WHERE PlantillaId IS NOT NULL` (sin eso un reintento reenviaría como texto libre los salientes con plantilla previos). `has-pending-model-changes` limpio. **Aplicada a `.\SQLEXPRESS/RRHH_WhatsApp`** |
| [x] | T1.07 Cola de envíos: servicio | T1.06 | 2026-09-15 · Build 0/0, 447 ok / 13 omitidas; +8 `ColaDeEnviosTests` (misma clave una fila, menú serializado, analista reservado no lo toma el lote, orden de toma, vencido → ambiguo, reciente intacto, acuse sobre `Enviando` avanza, acuse atrasado no retrocede) y +3 `ColaDeEnviosSqlTests` (índice único real, filas sin clave conviven, traducción a nulo), SQL ok. `OrdenAvance` reemplaza la comparación numérica |
| [x] | T1.08 Validador y despacho | T1.07, T0.03 | 2026-09-15 · Build 0/0, 454 ok / 13 omitidas (+7 `DespachoEnviosTests`: texto, menú como botones, plantilla inactiva → Permanente, ventana cerrada al despachar → Permanente, 503 → Transitorio con próximo intento a 60 s, ambiguo sin agenda, recupera `Enviando` atascados; prueba primero). **Desvíos:** `ProcesarAsync(int maximo, TimeSpan timeoutEnviando, ct)` (el timeout es opción del Worker); `IPlantillaService.ObtenerPorIdAsync` (el validador lee `Activa` de la base: el reintento buscaba por clave `""` si la navegación venía nula). `ReintentoEnvios` usa `ValidadorEnvio` + `EnviarSegunTipoAsync` (reintenta también botones y listas). `ProveedorFalso` compartido en `tests/.../Proveedores/` |
| [x] | T1.09 Worker de despacho | T1.08 | 2026-09-15 · Build 0/0, 455 ok / 13 omitidas (+1 en `SaludWorkerTests`: sin latido del despachador el health queda en rojo). `ServicioDespachoEnvios` (sin dormir con lote lleno; tolerancia de latido ≥ 3× el timeout de Enviando), opciones `IntervaloDespachoSegundos` 2 / `TamanoLoteDespacho` 20 / `TimeoutEnviandoSegundos` 120 en `OpcionesWorker` y `appsettings.json`, registrado después de la guardia. **Nota:** `ServiciosVigilados` vive en `Domain/Entidades/LatidoServicio.cs`, no en `Worker/Latido.cs`; `ChequeoWorker` lo toma solo de `ServiciosVigilados.Todos`. `EntornoDeReglas.Despacho` y paso `Despachar()` del arnés ya activos |
| [x] | T1.10 El ejecutor encola | T1.09 | 2026-09-15 · Build 0/0, 456 ok / 13 omitidas (+1: el ejecutor no recibe `IWhatsAppProvider`). `ContextoRegla.ClaveEjecucion`; `Para*` reciben `claveEjecucion`; `ProcesadorOutbox` → `evt:{EventoId}`, `BarridoTiempo` (con `TimeProvider`) → `barrido:{id}:{yyyyMMddHHmm}`, `EnvioAnalista` → `ana:{correlationId}`; el ejecutor encola `{clave}:{índice}` texto, plantilla, botones y lista. **Decisión sobre la clave del barrido:** se usa la de 04, no la de 03 (`{regla}:{marca}`), porque `AccionRegla` no lleva la regla de origen hasta T2.11; la unicidad entre barridos la dan los sellos, y T1.12 pone el barrido en transacción para que envío y sello se confirmen juntos. `EntornoDeReglas.ConsumirOutboxAsync(despachar = true)` y `DespacharAsync()`; el arnés llama con `despachar: false` |
| [x] | T1.11 Respuesta del analista idempotente | T1.10 | 2026-09-15 · Build 0/0, 459 ok / 13 omitidas (+3 en `EnvioAnalistaTests`: misma clave → un mensaje y un envío; sin clave dos respuestas salen; tipo y clave guardados; prueba primero). `EnvioAnalista`: consulta por clave → R15 → encola **reservado (`Enviando`)** → `DespachoEnvios.EnviarSegunTipoAsync` → marca resultado → R2. `ClaveIdempotencia` es `Guid?` opcional: `Guid.Empty` equivale a sin clave (una bandeja vieja no queda bloqueada). `PeticionResponder.ClaveIdempotencia = default`; `CajaRespuesta` genera el Guid al redactar y lo renueva solo si el envío salió. Sin `IWhatsAppProvider` en `EnvioAnalista` |
| [x] | T1.12 Consumidor transaccional y motor estricto (E16) | T1.10 | 2026-09-15 · Build 0/0; **475/475 con SQL**, 461 ok / 14 omitidas sin SQL. `ConsumidorOutbox.IntentarAsync` en `IUnidadTrabajo` (procesar + marcar). `MotorReglas` relanza (V35), comentario falso corregido; `MotorReglasTests` pasa a esperar la excepción. **Agregado:** `BarridoTiempo` también en transacción (envío encolado + sellos juntos; ver nota de T1.10). E16 (3): mismo evento dos veces → 1 mensaje, 1 envío; auditoría que falla después de encolar el enlace (R01 + R09) + reintento → 1 envío; **contra SQL** el intento fallido no deja asignación, invitación, enlace encolado ni auditoría, y el reintento envía una vez. `EntornoDeReglas`: `decorarAuditoria`, `ReglasEnOrden()`, consumo transaccional |
| [x] | T1.13 Alertas: modelo, migración `AlertasOperativas` | T1.01 | 2026-09-15 · Build 0/0. `AlertaOperativa` + `TiposAlerta` (se agregó **`MenuTruncado`**, que T1.14 necesita y la lista no traía), `IAlertaOperativaService` (Registrar con upsert y reintento ante 2601/2627, ListarAbiertas, Resolver → `bool`), `AlertaOperativaConfig` con `IX_AlertasOperativas_AbiertaUnica` filtrado y FK a `Analistas`. Migración revisada y **aplicada a `.\SQLEXPRESS`**. +7 pruebas (5 en memoria: agrupa con 2 ocurrencias, claves distintas, resuelta no absorbe, resolver dos veces, detalle recortado; 2 SQL: índice filtrado real, agrupación), todas ok |
| [x] | T1.14 Alertas en lugar de eventos huérfanos | T1.13, T1.10 | 2026-09-15 · Build 0/0; **486/486 con SQL**, 470 ok / 16 omitidas sin SQL. `EjecutorAcciones` registra alertas (`plantilla:{clave}`, `hc:{id}`, `menu`) en lugar de los 4 eventos; acción nueva `RequierePlantilla` en R15; `EnvioAnalista` la detecta y **audita** el intento rechazado (`EnvioRequierePlantilla`, con el analista) —así queda el registro que pedía la prueba y ARQ-09 («basta con la auditoría»)—; se borraron las 5 constantes de `TiposEvento`. Migración de datos `LimpiezaEventosHuerfanos` (borra los pendientes de esos tipos; aplicada). `Api/Salud/ChequeoAlertas.cs` registrado sin tag `listo` (Degraded). Pruebas: R15 (acción), R09 enlace y seguimiento (alertas), `EnvioAnalistaTests` (auditoría), +4 `ChequeoAlertasTests`; `ArnesEscenario.Alertas()` ya lee `AlertasOperativas`. **Cierra el Bloque 1** |
| [x] | T2.01 CalendarioLaboral | T0.02 | 2026-09-15 · Build 0/0, 479 ok / 16 omitidas (+9 `CalendarioLaboralTests`, prueba primero: viernes 20:00 → inicio viernes 18:00 y apertura lunes 09:00; jornada partida; sin tramos → null; periodo que cruza la medianoche; bordes; minutos hábiles; describir). `Domain/Calendario/ZonaHorariaPeru.cs` (movido con `git mv`) y `CalendarioLaboral.cs`. Firmas con `IReadOnlyCollection<HorarioAtencion>` en vez de `IReadOnlyList` (lo que el servicio ya tiene). Sin decisión de `arquitecto`: la ubicación ya la fijó V31 |
| [x] | T2.02 Fachada de horario | T2.01 | 2026-09-15 · Hecha junto con T2.01. `IHorarioAtencionService.InicioPeriodoFueraDeHorarioAsync` y `ProximaAperturaAsync`; `HorarioAtencionService` carga tramos y delega (sin lógica duplicada); `ReportingDbContext.HorariosAtencion` de solo lectura. `HorarioAtencionServiceTests` sin cambios, en verde |
| [x] | T2.03 Estados de conversación: dominio | T0.02 | 2026-09-15 · `EnMenuBot = 6`, `Cerrada` documentado como reservado, 7 campos nuevos en `Conversacion` y estado inicial `EnMenuBot`. Hecha junto con T2.04–T2.05 para no dejar pruebas rojas entre tareas |
| [x] | T2.04 Migración `SeguimientoConversacion` | T2.03 | 2026-09-15 · Revisada (aditiva, `IX_Conversaciones_EstadoActividad` con nombre, sin choque). SQL de datos de la especificación + `Down`. **Agregado:** backfill de `FechaEscalamiento` de las conversaciones `Escalada` desde la última auditoría `Escalamiento` (sin eso nunca llegarían al segundo nivel, A9). `MigracionSeguimientoConversacionSqlTests`: migra una base propia hasta `LimpiezaEventosHuerfanos`, siembra con el esquema viejo y verifica tras migrar. **Aplicada a `.\SQLEXPRESS`** |
| [x] | T2.05 Estados: servicio, acceso y bandeja | T2.04 | 2026-09-15 · Build 0/0; **499/499 con SQL**. `ObtenerOCrear` → `EnMenuBot`; matriz de acceso de V30 (quien atiende Total; Sistemas Lectura, incluso `EnMenuBot`; `PendienteClasificar` Lectura solo para rol `Analista`; resto Ninguno); `LimpiarCuentaContexto` → `EnMenuBot` + reinicio de intentos; `AsignarAnalista` saca de `EnMenuBot`/`PendienteClasificar` y limpia plazos; `ListarParaAnalista` excluye `EnMenuBot`; R01 y R14 tratan `EnMenuBot` igual. Pruebas: `VisibilidadTests` (matriz nueva + `EnMenuBot`), `RecepcionWebhookTests`, `R09RepreguntaEmpresaTests`, `E01PrimerMensajeTests` (2, parte de estado). `FiltroAccesoConversacionTests` no cambió (prueba niveles, no la matriz). **Recordatorio:** hasta T3.14 nadie puede responder en «Sin clasificar»; no publicar entre T2.05 y T3.14 |
| [x] | T2.06 Migración `DesenlaceYCierre` | T0.02 | 2026-09-15 · Build 0/0. `EstadoPostulacion.Reingreso = 5`, `EtapaKanban.EstadoResultante`, `Postulacion.CierreCortesiaPendiente/FechaCierreCortesia/FechaAvisoArchivado/FechaReingreso`; semilla etapa 4 → Contratado, 5 → Descartado. Migración revisada: `UpdateData` 4→2 y 5→3, las demás null; `CierreCortesiaPendiente` nace `false`, así que los descartes previos **no** disparan cierres en masa cuando llegue T4.09. +1 `EtapasKanbanSemillaTests`. Aplicada a `.\SQLEXPRESS` (el primer intento dio timeout transitorio; sin bloqueos al revisar, el reintento aplicó) |
| [x] | T2.07 Migración `CodigoAvisoVacante` | T0.02 | 2026-09-15 · Build 0/0. `Hc.CodigoAviso` (12) con `IX_HC_CodigoAviso` único filtrado. **Desvíos del backfill:** (1) alfabeto sin ambiguos ya en la migración (`23456789ABCDEFGHJKMNPQRSTUVWXYZ`, sin 0/O ni 1/I/L), no hex de `NEWID()`; (2) asignación a todas las filas de una vez + anulación de colisiones, en vez de un bucle por fila. `MigracionCodigoAvisoSqlTests` (40 vacantes → 40 códigos únicos con el alfabeto). **Aplicada a `.\SQLEXPRESS`**: las 3 vacantes locales tienen código. **Incidente:** durante un rato SQL Server local respondió muy lento (un `ALTER TABLE` tardó 9,8 s, rechazo de inicios de sesión) y dos aplicaciones de migración superaron el tiempo de espera; se revirtieron limpias y se reaplicaron. No era carga de CPU del equipo. La prueba corre en 2 s en condiciones normales |
| [x] | T2.08 Migración `VencimientoTransferencias` | T0.02 | 2026-09-15 · Build 0/0. `EstadoTransferencia.Vencida/Retirada`, `Transferencia.FechaVencimiento`, `IX_Transferencias_PendienteUnica`; `TransferirAsync` traduce la violación de unicidad al mismo mensaje de negocio y desprende lo agregado. **Hallazgo V17 repetido:** la primera migración generada **borraba `IX_Transferencias_ConversacionId`** (EF tomaba el índice filtrado como índice de la FK); se declaró el índice sin filtro con nombre y se regeneró. Limpieza previa de pendientes duplicadas (se conserva la más reciente, las demás pasan a Rechazada). `TransferenciasSqlTests`: 5 vueltas de dos transferencias simultáneas, siempre una ok y una con mensaje de negocio. **Infra de pruebas:** `CommandTimeout` de 300 s en `SqlServerFixture` y en las pruebas de migración, y en `RrhhDbContextFactory` (solo herramientas), por el SQL Express local con presión de memoria (69 MB en uso, 27% de utilización). Aplicada a `.\SQLEXPRESS` |
| [x] | T2.09 Migración `IndicesYRetorno` | T0.02 | 2026-09-15 · Build 0/0. `IX_EventosSistema_Cola` (Estado, Tipo, FechaCreacion) reemplaza al anterior; `IX_JobFormsRespuestas_Invitacion` único filtrado (reemplaza al índice de la FK: con filtro `IS NOT NULL` cubre toda búsqueda por FK, a diferencia de T2.08); `Ausencia.FechaAvisoRetorno`. Al inicio de `Up`, un `THROW 50001` con las invitaciones afectadas si hay respuestas duplicadas: se detiene y no borra nada. `MigracionIndicesYRetornoSqlTests` lo verifica (la migración no queda aplicada y los datos siguen). Sin duplicados en local; **aplicada a `.\SQLEXPRESS`**. **Entorno:** la máquina tiene 7,9 GB de RAM con ~0,7 GB libres; SQL Express llegó a fallar por falta de memoria. Apagar los servidores de compilación (`dotnet build-server shutdown`) liberó ~1 GB y la prueba pasó en 7 s |
| [x] | T2.10 Migración `ParametrosAtencionPreferente` | T0.02 | 2026-09-15 · Build 0/0. 9 constantes en `ClavesConfiguracion` con su descripción, sembradas con los valores de 03 §1.1; descripción de `envio.maximo_por_segundo` con «por proceso emisor» (pendiente de T0.04). Migración revisada: 9 `InsertData` + 1 `UpdateData`. +1 prueba en `ParametrosReglasTests`: toda constante de `ClavesConfiguracion` está en la semilla del modelo. Aplicada a `.\SQLEXPRESS`. `horario.descripcion` sigue leyéndose como literal en R03 hasta COR-01 (T3.02) |
| [x] | T2.11 Contexto y acciones: dominio | T2.03, T2.06, T2.08, T1.10 | 2026-09-15 · Build 0 errores, 8 advertencias CS0618 esperadas (R09RepreguntaEmpresa, R16Archivado y la fábrica siguen con lo obsoleto hasta T3.09). `ContextoRegla`: las 12 propiedades de 03 §ARQ-07, record `PostulacionVigente`, enum `OrigenEleccion`, derivados `TieneProcesoVivo`, `CuentasVivas` (sin repetir, en orden) y `DiasDesdeActividadAnterior`. `PaginaMenu` arranca en **0** y no en 1: FUN-03 distingue «pidió otra página» con `PaginaMenu > 0`. Acciones nuevas + `MarcaConversacion`/`MarcaPostulacion`; `MostrarMenuEmpresas(EsReintento, Pagina = 1)`; `EscalarARespaldo(AnalistaEsperadoId, AnalistaRespaldoId, Motivo)` y R02 ya pasa el que atendía (el ejecutor lo ignora hasta T3.15). `SoloSiSeEnvioAnterior` queda para T2.14, junto con el ejecutor que lo respeta. `IdsBoton`: `pag_`, `proc_`, `otra_empresa` con `Para*`, `Leer*` y `EsOtraEmpresa`. +2 archivos de prueba en `Dominio/` (`ContextoReglaTests`, `IdsBotonTests`). Suite completa con SQL: 522/522 |
| [x] | T2.12 Payload mínimo con instantánea | T2.11, T1.03 | 2026-09-15 · `RecepcionWebhook` toma `FechaUltimaActividad` dentro de la transacción y antes de `RegistrarEntranteAsync` (COR-05 paso 2); el evento queda con `ConversacionId`, `MensajeId`, `IdBotonPulsado` y `FechaActividadAnterior`, sin teléfono, texto ni nombre de perfil. `ProcesadorOutbox.PayloadMensajeEntrante` con los 4 campos; `ParaMensajeEntranteAsync(conversacionId, mensajeId, idBotonPulsado, fechaActividadAnterior, …)`, y la fábrica ya carga `MensajeEntrante` desde la tabla (lo necesita T3.07) y pasa la instantánea. +3 pruebas en `RecepcionWebhookTests`: el payload no trae datos personales y sus campos son exactamente esos 4; identifica mensaje y botón; el segundo entrante lleva la fecha del primero. Suite con SQL: 525/525 |
| [x] | T2.13 Fábrica con contexto nuevo | T2.12, T2.02, T2.07 | 2026-09-15 · La fábrica carga `PostulacionesDelPostulante` (join con `Cuenta` y `Hc`, ordenado por actividad), `InicioPeriodoFueraHorario`, `ProximaApertura`, `TransferenciaPendiente`, los tres plazos en minutos hábiles (`FechaEscalamiento`, `FechaPendienteDesde`, `FechaTextoNoReconocido`) y `PaginaMenu`/`PostulacionElegidaId` desde `IdsBoton`. `OrigenEleccion`: `Boton` si el botón trajo cuenta o vacante, `Contexto` si la cuenta ya venía en el hilo, `Ninguna` si no hay nada (`CodigoAviso` y `NombreCuenta` quedan para T3.07). Nuevo `ParaTiempoPostulacionAsync(postulacionId, claveEjecucion, ct)` (FUN-10, FUN-11), con la conversación más reciente del postulante. `ConstructorContexto` suma `ConProcesos`/`Proceso`, `ActividadAnterior`, `EligioPor`, `PidioPagina`, `EligioProceso`, `ConTransferenciaPendiente`, `CierreSolicitado`, `FueraDeHorarioDesde` y `MinutosHabiles`. Nuevo `FabricaContextoReglaTests` con 15 pruebas. Suite con SQL: 540/540 |
| [x] | T2.14 Ejecutor con acciones nuevas | T2.13, T1.14 | 2026-09-15 · `IConversacionService`: `SellarAsync`, `RegistrarIntentoMenuAsync`, `ReiniciarIntentosMenuAsync`, `DerivarAPendientesAsync` (auditoría `DerivadaABandejaGeneral`), `ReactivarAsync`, `TomarContextoDePostulacionAsync` (analista asignado o titular). `IPostulacionService`: `ArchivarAsync`, `SellarAsync`. `IAnalistaService.ListarActivosPorRolAsync`. El ejecutor suma `IAnalistaService` y un `case` por acción nueva; `NotificarRol` publica un `AnalistaNotificado` por analista activo del rol y deja error en el log si no hay ninguno. `EnviarMensajeBot`: texto dentro de la ventana, plantilla activa fuera, alerta `PlantillaNoAprobada` y **no ejecutada** si no hay ninguna. Los envíos devuelven si encolaron y el ejecutor lleva `ultimoEnvioEncolado`: `SellarConversacion`, `SellarPostulacion` y `MarcarRecordatorioJobForms` con `SoloSiSeEnvioAnterior` se saltan si el envío anterior no salió. `MostrarMenuProcesos` arma los procesos vivos (nombre de cuenta, con vacante si la cuenta repite) más «Otra empresa». `VencerTransferencia` queda sin `case` hasta T4.04, que es donde nace `VencerTransferenciaAsync`. Nuevo `EjecutorAccionesNuevasTests` con 23 pruebas. Suite con SQL: 563/563 |
| [x] | T3.01 Mensajes del bot en texto (E04 parte 1) | T2.14 | 2026-09-15 · `Application/Reglas/TextosBot.cs` con los cinco textos en paralelo a los borradores de `DatosSemilla`; sin nombre el saludo va seco («Hola.») en vez de decir «Hola hola». R09 confirmación, R09 seguimiento, R12 y R20 pasan a `EnviarMensajeBot(texto, clave, parámetros)`; el recordatorio sella con `SoloSiSeEnvioAnterior = true`. Nuevo `E04ConfirmacionSinPlantillasTests`: con las 6 plantillas inactivas la confirmación sale en texto; el recordatorio fuera de ventana y sin plantilla no sale, deja alerta y **no** se sella; aprobada la plantilla, sale y se sella. Pruebas viejas que documentaban el sellado ciego y el envío por plantilla dentro de la ventana, actualizadas (R09 seguimiento, R20, `AccionesBandejaTests`, `RecepcionJobFormsTests`). `TextosBot.FueraDeHorario` queda listo para T3.02. Suite con SQL: 567/567 |
| [x] | T3.02 R3 reescrita (E01) | T3.01 | 2026-09-15 · `R03FueraDeHorario` según 03 §FUN-04: `Aplica` compara `FechaAvisoFueraHorario` con `InicioPeriodoFueraHorario` (un aviso por período, no uno cada 8 h) y no avisa si no hay horario cargado; el resultado es `EnviarMensajeBot(TextosBot.FueraDeHorario(...), FueraDeHorario, [descripcion])` más `SellarConversacion(AvisoFueraHorario) { SoloSiSeEnvioAnterior = true }`. Fuera el literal de 8 h y la clave `horario.descripcion`: la descripción sale de `CalendarioLaboral.Describir` vía `DescripcionHorario` en el contexto. Nuevo `E01FueraDeHorarioTests` (**verificado que falla contra la R3 anterior**: 2 de 3 pruebas en rojo antes del cambio): viernes 20:00 con tres mensajes da un aviso que dice «lunes» y «09:00»; el lunes 19:00 da otro; dentro del horario, ninguno. `ArnesEscenario.ConHorarioComercialAsync()` siembra lunes a viernes 09:00–18:00. `R03FueraDeHorarioTests` rehecho (7 casos). Suite con SQL: 573/573 |
| [x] | T3.03 Menú con opciones válidas | T1.14 | 2026-09-15 · `ICuentaService.ListarConVacantesAbiertasAsync` pasa a `ListarMenuAsync`: cuenta activa + titular activo + vacante abierta con `UrlJobForms` cargada. `CuentaService` recibe `IAlertaOperativaService` y `CrearVacanteAsync` deja `VacanteSinFormulario` cuando la URL viene vacía (la vacante se crea igual: el analista suele no tener el enlace a mano). `FabricaContextoRegla.VacantesAbiertasAsync` filtra las vacantes sin URL, así que ya no se ofrece ni se manda un enlace muerto; el aviso al postulante pasa a ser el de la R20. +6 pruebas en `AdministracionCuentasTests`; `R09EnvioLinkTests` y `R20VacanteCerradaTests` ajustadas al menú más estricto. README: la frase sobre el menú dice las tres condiciones. Suite con SQL: 579/579 |
| [x] | T3.04 R9 enlace sin menú repetido (E04 parte 2) | T2.13, T3.03 | 2026-09-15 · `R09EnvioLink` según 03 §COR-02: `Aplica` deja fuera el hilo `Activa` con analista y proceso vivo en la cuenta; solo la elección hecha **en este mensaje** (`OrigenEleccion` Boton o CodigoAviso) manda el enlace de esa vacante; si no eligió y ya hay invitación de alguna vacante abierta de la cuenta o proceso vivo, `SinAccion`. Desvío anotado: con una sola vacante abierta y nada empezado se manda el enlace directo en vez del menú de una opción (era el comportamiento previo y 03 §COR-02 solo describe el caso de varias). Nuevo `E04MenuVacantesTests`: con dos vacantes, un solo menú en todo el hilo (formulario y tres preguntas después); una vacante nueva no le abre menú a quien ya está en proceso. Suite: 581 (20 SQL omitidas en esta corrida) |
| [x] | T3.05 R19 contador persistente (E05 parte 1) | T2.14, T2.10 | 2026-09-15 · `R19FallbackMenu` según 03 §COR-06: `Aplica` exige `EnMenuBot` (en «Sin clasificar» ya no interviene, B6); reintentos desde `menu.reintentos_permitidos` (fuera la constante); emite `RegistrarIntentoMenu(TextoNoReconocido: intentos > 0)` y, agotados, `DerivarAPendientes`. Pedir otra página (`PaginaMenu > 0`) devuelve el menú sin contar intento (el ejecutor la pagina en T3.06). Fuera `ContarIntentosMenuAsync`: `IntentosMenuFallidos` sale de la conversación. R01 y R14 emiten `ReiniciarIntentosMenu()` cuando el hilo pasa a una persona. Nuevo `E05MenuNoReconocidoTests`: «Hola» → menú, «xx» → reintento, «yy» → `PendienteClasificar` con `FechaPendienteDesde`; y un hilo con 30 entrantes al que se le limpia el contexto vuelve a ver el menú en vez de ir directo a la bandeja general. Suite con SQL: 586/586 |
| [x] | T3.06 Menú paginado (E03) | T3.05 | 2026-09-15 · `EjecutorAcciones.Paginar`: con más de 10 cuentas, 9 por página más la fila «Ver más empresas» (`pag_{n+1}`), y «Volver al inicio» (`pag_1`) en la última; constante `FilasPorPaginaConNavegacion = MaximoOpcionesMenu - 1`, comentada como límite de Meta. `EnviarMenuAsync` ya no trunca ni alerta: un menú de más de 10 filas ahora es excepción, porque quien lo arma tiene que haberlo paginado. Se quitó `TiposAlerta.MenuTruncado` (y sus usos en pruebas). Nuevo `E03MenuPaginadoTests` con 21 cuentas: página 1 con 9 + «ver más», última con 3 + «volver», navegar no deriva a «Sin clasificar» ni suma intentos, y con pocas cuentas no se pagina. README: sección «El menú del bot» y alertas sin «truncado». Suite: 590 (20 SQL omitidas) |
| [x] | T3.07 Código de aviso y texto (E02) | T3.06, T2.07 | 2026-09-15 · `Domain/Entidades/CodigoAviso.cs`: `Alfabeto` (el mismo de la migración T2.07, sin I, L ni O porque se confunden con 1 y 0 al copiar de un aviso), `Generar`, `EsValido` (4–12 alfanuméricos), `Candidatos` y `Normalizar` (mayúsculas, sin tildes, signos como separador). `ICuentaService`: `BuscarVacantePorCodigoAsync(candidatos)` —devuelve la vacante aunque esté cerrada, para que entre la R20— y `BuscarCuentaDeMenuPorNombreAsync(textoNormalizado)`, que compara en memoria sobre las ~20 cuentas del menú para no depender de la intercalación de SQL. `CrearVacanteAsync` genera el código único con reintento. La fábrica reconoce, sin botón: primero código (`CodigoAviso`), luego nombre de cuenta (`NombreCuenta`), y recién después hereda el contexto. `R19FallbackMenu.Aplica` exige `OrigenEleccion == Ninguna`. Nuevos `CodigoAvisoTests` (13) y `E02CodigoAvisoTests` (4): el código manda el formulario sin menú, el de una vacante cerrada da el aviso de la R20, el nombre de la empresa también identifica y un texto cualquiera sigue dando menú. Suite con SQL: 607/607 |
| [x] | T3.08 Enlace de aviso en administración | T3.07 | 2026-09-16 · `Api/Configuracion/OpcionesWhatsApp.cs` (`NumeroPublico` + `EnlaceDeAviso(codigo)`, que limpia el número escrito a mano y devuelve **nulo** si falta configurarlo), registrada en `Program`. `VacanteResumen` suma `CodigoAviso`; nuevo record `EnlaceAviso(Codigo, Enlace)`; `GET /hc/{id}/enlace-aviso` con el mismo permiso que editar la vacante (403 ajena, 404 inexistente, 422 sin código). `ClienteApi.EnlaceAvisoAsync` y, en `Vacantes.razor`, columna «Código del aviso» con botón «Copiar enlace» (portapapeles por JS con campo de respaldo) y aviso cuando falta el número. **Número dado por el usuario: `+1 (555) 665-0366`** → normalizado a `+15556650366`; es el número de **prueba** de Meta (lista blanca y caduca), así que queda solo en `appsettings.Development.json`; `appsettings.json` lo deja vacío y el checklist de producción exige la línea definitiva. Nuevo `EnlaceAvisoTests` (9 casos). Suite con SQL: 647/647. **Checklist manual pendiente:** ver el código y el enlace en la pantalla de vacantes |
| [x] | T3.09 R9 repregunta con continuidad (E11, E12) | T2.13, T3.05 | 2026-09-15 · `R09RepreguntaEmpresa` según 03 §COR-07: mide `DiasDesdeActividadAnterior` (instantánea del webhook, en cualquier dirección, A13), no repregunta si hay proceso vivo en la cuenta en contexto (`CuentasVivas`), un `Descartado` ya no bloquea, y suma `ReiniciarIntentosMenu`. No aplica si el mensaje trajo código o nombre de empresa (FUN-02). `R16Archivado` usa `TieneProcesoVivo` + `Contratado`. **Borrados** `EstadosPostulaciones` y `DiasDesdeMensajeAnterior` del contexto y sus dos consultas en la fábrica: build sin advertencias CS0618. Nuevo `E11E12RepreguntaTests`: en proceso a los 5 días sigue `Activa` con su analista y sin menú; descartado a los 5 días vuelve a `EnMenuBot` con menú; al día siguiente no pasa nada. `R09RepreguntaEmpresaTests` rehecho: el hueco se produce moviendo el reloj, no reescribiendo fechas de mensajes. Suite con SQL: 610/610 |
| [x] | T3.10 R14 respeta lo asignado (E10) | T2.14 | 2026-09-15 · `R14Ausencias` según 03 §COR-08: asigna el respaldo solo si nadie atiende o si atendía el titular ausente (una transferencia o un escalamiento ya no se deshacen con el siguiente mensaje); `EstablecerCuentaContexto` solo si cambia; sin respaldo, `DerivarAPendientes` para que quede el plazo (`FechaPendienteDesde`). Nuevo `E10AusenciaTransferenciaTests`: transferida a un tercero sigue con él aunque el titular se ausente; sin nadie atendiendo sí va al respaldo; sin respaldo queda en «Sin clasificar» con plazo. +3 casos en `R14AusenciasTests`. El aviso multi-cuenta compartido con la R01 (COR-10) queda para T3.11. Suite con SQL: 616/616 |
| [x] | T3.11 Aviso multi-cuenta una sola vez | T3.10 | 2026-09-15 · `Application/Reglas/AvisoMultiCuenta.cs` con `Acciones(ctx, analistaDestinoId, notificarOtrasCuentas)`, sobre `PostulacionesDelPostulante` vivas de otras cuentas. La R01 avisa solo cuando **asigna** o cuando el disparador es `JobFormsCompletado`; en ese caso avisa además a los analistas asignados de las otras cuentas (Regla 6: un aviso por analista, no por postulación). La R14 usa el mismo helper al asignar el respaldo. Se retiró `ContextoRegla.OtrasCuentasEnProceso` y su consulta en la fábrica, que quedaron sin lector (el endpoint de la bandeja sigue con `ObtenerOtrasCuentasEnProcesoAsync`). +3 casos en `R01AsignacionTests` y nuevo `AvisoMultiCuentaTests` sobre el circuito: tres mensajes seguidos dan un solo aviso; completar el formulario avisa a los dos analistas. Suite con SQL: 620/620 |
| [x] | T3.12 Kanban con desenlace explícito | T2.06 | 2026-09-15 · `MoverEtapaKanbanAsync` devuelve `ResultadoMovimiento(Anterior, Nuevo, Aplicado)` y fija el estado por `EtapaKanban.EstadoResultante`: salir de una columna final revierte a `EnProceso` y un `Reingreso` se conserva. Fuera los `StartsWith("Contratado"/"Descartado")`; `EtapaFinalDescartadoAsync` busca por `EstadoResultante`. `AccionesBandeja.MoverEtapaAsync` publica `PostulacionDescartada` solo si el movimiento se aplicó y **pasó** a descartada (antes lo publicaba por el estado final, así que tocar una tarjeta ya descartada repetía el cierre). Nuevo `E14KanbanTests` (parte de estado): Descartado → Entrevista vuelve a `EnProceso`; volver a Descartado no publica de nuevo; reconsiderar y descartar otra vez sí; Contratado cierra sin publicar descarte. Suite con SQL: 624/624 |
| [x] | T3.13 Tomar: servicio y bloqueos | T2.05, T1.01 | 2026-09-15 · `IConversacionService.TomarAsync(conversacionId, analistaId, cuentaId)`: exige `PendienteClasificar` y acceso `Total` a la cuenta (`ICuentaService` inyectado en `ConversacionService`), fija cuenta, analista y `Activa`, limpia `FechaPendienteDesde` y el contador del menú, y audita `TomadaDeBandejaGeneral`. Nueva `Domain/Excepciones/ConflictoConcurrenciaException` para el caso en que otro la tomó primero (la Api la traduce a 409 en T3.14); acceso ajeno da `UnauthorizedAccessException`. `MotivosBandeja.TomarPrimero` en Domain, compartido por `EnvioAnalista` (responder sin tomar da `Enviado = false`), `TransferirAsync` y `ConversacionesController.Marcar`. Desvío anotado: el servicio no envuelve en `IUnidadTrabajo` porque ya es un único `SaveChanges`, y la carrera la resuelve `RowVersion`. Nuevo `TomarConversacionTests` (7 casos). `TransferenciasSqlTests` ajustado: el hilo nace `EnMenuBot` y ahora hay que tomarlo antes de transferir. Suite con SQL: 631/631 |
| [x] | T3.14 Tomar: Api y bandeja (E07) | T3.13 | 2026-09-15 · `PermiteTomarAttribute` + `FiltroAccesoConversacion`: la acción marcada pasa con nivel `Lectura` (el hilo todavía no es de quien lo toma) y el servicio sigue exigiendo que trabaje la cuenta. `PeticionTomar(CuentaId)` en Contracts; `POST /conversaciones/{id}/tomar` responde 200 con el resumen, 409 por `ConflictoConcurrenciaException`, 403 sin acceso y 422 si ya no está en «Sin clasificar». `ClienteApi.TomarAsync`; `PanelChat.razor` muestra selector de cuentas y botón «Tomar» en vez de la caja de respuesta cuando el estado es `PendienteClasificar`, con el motivo si otro llegó primero. `EstadosConversacion` en Contracts, para que el Frontend no compare literales sueltos. Nuevo `E07TomarTests` (SQL): dos tomas concurrentes dan un ganador y un rechazo, y la fila queda con el ganador. +3 casos en `FiltroAccesoConversacionTests` y `Tomar` declarado en `AutorizacionTests`. **Checklist manual pendiente en la bandeja** (T3.14 pide verlo en pantalla). Suite con SQL: 635/635 |
| [x] | T3.15 Escalamiento sin carrera | T2.14 | 2026-09-15 · `EscalarAsync(conversacionId, analistaEsperadoId, analistaRespaldoId, motivo)`: revalida que el titular no haya respondido (`FechaUltimaRespuestaAnalista >= FechaUltimoMensajeEntrante`) y que siga atendiendo el analista esperado; si no, no escala y lo deja en el log. Sella `FechaEscalamiento` y limpia `FechaAvisoSegundoNivel` (FUN-05). La R02 pasa `AnalistaEsperadoId` y ya no emite `CambiarEstadoConversacion(Escalada)`, que duplicaba lo que hace el servicio. Nuevo `EscalamientoSinCarreraTests`: escala sin respuesta; no escala si el analista respondió entre medio; no escala si el hilo cambió de manos. Suite con SQL: 638/638. **Bloque 3 cerrado salvo T3.08**, que espera `WhatsApp:NumeroPublico` |
| [x] | T4.01 Segundo nivel (E08) | T3.15, T2.13 | 2026-09-16 · `R02SegundoNivel` (prioridad 26): aplica con `Escalada`, `FechaEscalamiento` puesta, `FechaAvisoSegundoNivel` nula y el postulante esperando; con `MinutosHabilesDesdeEscalamiento >= escalamiento.horas_segundo_nivel * 60` avisa a **Jefatura por rol**, al respaldo y al titular, sella `AvisoSegundoNivel` y audita. `ListarPendientesSegundoNivelAsync` como prefiltro y el Worker la suma a las candidatas; `RegistrarRespuestaAnalistaAsync` limpia `FechaAvisoSegundoNivel`, para que un escalamiento posterior vuelva a poder avisar. Registrada en `AgregarReglas` y en `EntornoDeReglas`; `RegistroDependenciasTests` pasa a esperar 14 reglas. Nuevo `E08SegundoNivelTests` (5 casos, con horario comercial sembrado): avisa a los tres, no repite por barrido, no avisa antes del plazo, el fin de semana no corre y responder detiene el plazo. Suite con SQL: 652/652 |
| [x] | T4.02 Marca «vencida» | T4.01 | 2026-09-16 · `ConversacionResumen` suma `bool Vencida` (con valor por defecto, para no tocar a quienes lo construyen); `MapeoBandeja` lo calcula con los mismos datos que la regla —`FechaAvisoSegundoNivel` puesta y el postulante todavía esperando—, así que la pantalla no puede decir otra cosa que el barrido. Distintivo rojo «vencida» en `ListaConversaciones.razor` y su estilo en `bandeja.css`. +3 pruebas en `MapeoBandejaTests`: con aviso y sin responder está vencida, sin aviso no, y responder la saca. **Checklist manual pendiente:** verlo en la bandeja. Suite con SQL: 655/655 |
| [x] | T4.03 Derivación por silencio y plazo (E05, E06) | T3.05, T2.13 | 2026-09-16 · `R19DerivacionPorSilencio` (prioridad 23): en `EnMenuBot` con `FechaTextoNoReconocido`, a las `menu.horas_derivacion` hábiles deriva a «Sin clasificar» y detiene. `R19AvisoPendiente` (24): en `PendienteClasificar` sin aviso previo, a las `clasificacion.horas_aviso` hábiles avisa a Jefatura por rol, sella `AvisoPendiente` y audita. Prefiltros `ListarPendientesDerivacionMenuAsync` y `ListarPendientesAvisoClasificacionAsync`, sumados a las candidatas del Worker; ambas registradas en `AgregarReglas` y `EntornoDeReglas` (16 reglas). Nuevo `E06SilencioTests` (6 casos: deriva tras el plazo, no antes, avisa a Jefatura, no repite el aviso, elegir antes del plazo no deriva, tomarla apaga el plazo) y +1 caso en `E05MenuNoReconocidoTests` (parte 2). Suite con SQL: 662/662 |
| [x] | T4.04 Vencimiento de transferencias (E09) | T2.08, T2.13 | 2026-09-16 · `CalendarioLaboral.SumarMinutosHabiles` (+4 pruebas: salta el fin de semana, dentro de jornada suma como el reloj, fuera de hora arranca en la apertura, sin tramos va a reloj corrido) y su fachada `SumarMinutosHabilesAsync`. `TransferirAsync` recibe `IAusenciaService`, `IHorarioAtencionService` y `IConfiguracionReglasService`: rechaza destino ausente y fija `FechaVencimiento` con `transferencia.horas_vencimiento` en horas hábiles (nula en las urgentes). `VencerTransferenciaAsync` (solo si sigue pendiente, estado `Vencida` + auditoría) y `ListarConversacionesConTransferenciaVencidaAsync`. Nueva `R08VencimientoTransferencia` (prioridad 21) que además avisa a origen y destino —la regla decide los avisos, el servicio solo cambia el estado—; `case VencerTransferencia` en el ejecutor y candidatas nuevas en el Worker (que ahora recibe `TimeProvider`). Nuevo `ServiciosDePrueba.Conversaciones(db, reloj)` para no repetir el grafo en ~10 pruebas. Nuevo `E09TransferenciaVenceTests` (7 casos). Suite con SQL: 673/673 |
| [x] | T4.05 Retiro de transferencias y bandeja | T4.04 | 2026-09-16 · `RetirarTransferenciaAsync` (solo el origen, solo si sigue pendiente; estado `Retirada`, auditoría y aviso al destino) y `ListarTransferenciasEnviadasPendientesAsync`. `POST /transferencias/{id}/retirar` y `GET /transferencias/enviadas`; `TransferenciaPendiente` suma `FechaVencimiento`, nuevo `TransferenciaEnviada`, y `AnalistaResumen` suma `Ausente` (lo calcula `AnalistasController.Listar`). Frontend: `TransferenciasEnviadas.razor` con botón «Retirar», el vencimiento visible en las recibidas, `AccionesRapidas` deja de ofrecer analistas ausentes y `ClienteApi` suma los dos métodos. Nuevo `RetiroTransferenciaTests` (7 casos: retira, avisa al destino, el destino no puede retirar, respondida no, inexistente da `KeyNotFound`, retirada no bloquea otra, y el listado). `Retirar` declarado en `AutorizacionTests`. **Checklist manual pendiente:** ver «Transferencias que enviaste» y retirar una. Suite con SQL: 680/680 |
| [x] | T4.06 Reingreso | T3.09, T3.12 | 2026-09-16 · `MarcarReingresoAsync`: estado `Reingreso`, `FechaReingreso`, primera etapa no final, apaga `CierreCortesiaPendiente` y audita. `POST /postulaciones/{id}/reingreso` con el mismo control de acceso que mover de etapa (declarado en `AutorizacionTests`). `R01Asignacion` suma el caso de FUN-08: sin cuenta identificada, sin elección y con **un único** proceso vivo, emite `TomarContextoDePostulacion` en vez de dejar que hable el menú; `R19FallbackMenu` deja de aplicar en ese caso. `ReportingReadModel` cuenta `Reingreso` como en proceso. Frontend: `ClienteApi.MarcarReingresoAsync`, botón «Reingreso» en la tarjeta del tablero y en el chip del chat; `EstadosPostulacion` en Contracts para no comparar literales. Nuevo `E13ReingresoTests` (4 casos: vuelve al tablero, escribe a los 10 días y va directo con su analista, no se archiva a los 120 días, y sin contexto con un solo proceso vivo se retoma sin menú). Suite con SQL: 684/684 |
| [x] | T4.07 Desambiguación multi-cuenta (E19 entrada) | T3.09, T3.11 | 2026-09-16 · `R06Desambiguacion` (prioridad 17): con dos o más cuentas vivas y sin elección en el mensaje, muestra el menú de procesos y **detiene**; solo interviene si el hilo no tiene contexto o si pasaron los días de `conversacion.repregunta_dias` (A13), para no preguntar en cada mensaje (P4). `ContextoRegla.PidioOtraEmpresa` (botón `otra_empresa`), que la R09 de repregunta trata como pedido explícito: limpia contexto y muestra el menú de empresas sin esperar plazo. `R01Asignacion` toma el contexto de `PostulacionElegidaId`. El menú de procesos titula «{Cuenta}: {Vacante}» recortado a 24 caracteres (límite de Meta: un título largo hace que Meta rechace el mensaje entero). Nuevo `E19MultiCuentaTests` (4 casos: pregunta con dos procesos, elegir uno deja cuenta y analista de esa postulación, «Otra empresa» lleva al menú, con uno solo no pregunta). 18 reglas registradas. Suite con SQL: 688/688 |
| [x] | T4.08 Prefijo multi-cuenta (E19 salida) | T4.07, T1.11 | 2026-09-16 · `Application/Reglas/PrefijoMultiCuenta.cs`: antepone «[Cuenta · Vacante] » solo si hay procesos vivos en más de una cuenta, tomando la postulación más reciente de la cuenta en contexto; no prefija dos veces (un doble clic reenvía el mismo texto, V29). `EnvioAnalista` lo aplica al texto libre —la plantilla no se toca, su contenido está aprobado por Meta—. Nuevo `PrefijoMultiCuentaTests` (7 casos) y +2 en `E19MultiCuentaTests`: con dos procesos la respuesta lleva el prefijo, con uno solo va tal cual. Suite con SQL: 697/697 |
| [x] | T4.09 Cierre controlado (E14, E15) | T3.12, T3.01 | 2026-09-17 · `PeticionMarcar` y `PeticionMoverEtapa` suman `EnviarCierre = true`; los controladores lo pasan y `AccionesBandeja` lo publica en el payload de `PostulacionDescartada`. `MarcarCierrePendienteAsync(id, enviar, automatico)` (no hace nada si ya tiene `FechaCierreCortesia`) y `ListarCierresPendientesAsync`. `ProcesadorOutbox` registra el pedido **dentro** de la transacción y antes de evaluar, leyendo `cierre.automatico`. `R12CierreCortesia` reescrita según 03 §FUN-10: aplica con el cierre pendiente y sin sello, en `CambioEstadoPostulacion` o `TiempoTranscurrido`; fuera de horario no hace nada y dentro envía `EnviarMensajeBot` + `SellarPostulacion(CierreCortesiaEnviado) { SoloSiSeEnvioAnterior = true }`. `BarridoTiempo.ProcesarPostulacionAsync` y el Worker recorre también las postulaciones con cierre pendiente; el arnés de escenarios barre ambas cosas. Nuevo `E15CierreFueraHorarioTests` (en horario sale una vez; sin pedirlo no sale; el viernes a la noche queda pendiente y el lunes 10:00 sale como plantilla aprobada) y +1 caso en `E14KanbanTests` (descartar dos veces no despide dos veces). Suite con SQL: 701/701 |
| [x] | T4.10 Cierre en la bandeja | T4.09 | 2026-09-17 · `AccionesRapidas.razor`: casilla «Enviar mensaje de cierre al postulante», marcada por defecto, solo en «Descartar», que viaja como `PeticionMarcar.EnviarCierre`. `Tablero.razor`: soltar en la columna de descarte abre una confirmación con la misma casilla antes de mover (el arrastre es fácil de hacer sin querer y la despedida no se deshace). La columna se reconoce por `EtapaTablero.EstadoResultante`, que el contrato suma y `VacantesController.Tablero` llena, en vez de por su nombre (COR-11). Suite con SQL: 701/701. **Checklist manual pendiente:** descartar desde el chat con y sin la casilla, y arrastrar una tarjeta a Descartado |
| [x] | T4.11 Archivado por postulación | T4.06, T4.09 | 2026-09-17 · `R16Archivado` pasa a ser **por postulación** según 03 §FUN-11: en curso o descartada, a los `conversacion.archivado_dias` sin actividad emite `ArchivarPostulacion` (el estado `Archivada` por fin se asigna, M6); a `conversacion.aviso_archivado_dias` del límite avisa al analista asignado —o al titular— con la fecha, una sola vez (`SellarPostulacion(AvisoArchivado)`). Contratado y Reingreso no se archivan. **Desvío anotado:** en vez de «`TiempoTranscurrido` con `Postulacion` presente», el barrido por postulación usa un disparador propio, `TipoDisparador.TiempoTranscurridoPostulacion`; con el mismo disparador, las reglas por tiempo del hilo (escalar, derivar, vencer transferencias) habrían corrido otra vez por cada postulación. La R12 pasa a ese disparador. El archivado del hilo se movió a `R16ArchivadoConversacion` (que T4.12 formaliza) y usa `CambiarEstadoConversacion(Archivada)` + auditoría; se retiró la acción `ArchivarConversacion` (ARQ-07). `ListarPorArchivarAsync` y `ListarPorAvisarArchivadoAsync` alimentan las candidatas por postulación del Worker. `R16ArchivadoTests` rehecho (8 casos por postulación) y nuevo `R16ArchivadoConversacionTests` (7 casos, con los del hilo que ya existían). 19 reglas. Suite con SQL: 710/710 |
| [x] | T4.12 Archivado de conversación y reactivación (E13) | T4.11 | 2026-09-17 · `R16ArchivadoConversacion` (prioridad 22, ya creada en T4.11): archiva el hilo sin procesos vivos ni contratados tras `conversacion.archivado_dias`. Nueva `R16Reactivacion` (prioridad 12, antes que todas las del entrante): un hilo archivado que recibe un mensaje ya no queda invisible (AL10). Con **un** proceso vivo vuelve `Activa` y toma el contexto de esa postulación; con varios vuelve al bot y deja que la R06 pregunte en la misma evaluación; sin ninguno vuelve a `EnMenuBot`, limpia el contexto y muestra el menú con saludo de regreso (`MostrarMenuEmpresas.EsRegreso`, «Hola de nuevo…») y detiene. Registrada en `AgregarReglas` y `EntornoDeReglas` (20 reglas). Nuevo `E13ArchivadoTests`: aviso a los 83 días y archivo a los 90 (primero la postulación, en la vuelta siguiente el hilo); vuelve a escribir y recibe el menú de regreso; con reingreso vuelve `Activa` con su analista y sin menú. Suite con SQL: 713/713 |
| [x] | T4.13 Aviso de retorno de ausencia | T2.09 | 2026-09-17 · Nuevo caso de uso `Application/Casos/AvisoRetornoAusencia.cs` (no es una regla: solo informa). Por cada ausencia terminada y sin aviso, cuenta las conversaciones de las cuentas del titular asignadas por ausencia en el período que siguen sin volver a él, y si hay alguna publica «Durante tu ausencia, {n} conversaciones nuevas quedaron con {respaldo}»; sella igual aunque no haya nada que contar. Cada ausencia en su transacción. `IAusenciaService` suma `ListarFinalizadasSinAvisoAsync` y `MarcarAvisoRetornoAsync` (`AusenciaService` recibe el reloj); el conteo es `IConversacionService.ContarAsignadasPorAusenciaAsync`, que cruza la auditoría `AsignacionPorAusencia` con las conversaciones **en la base** (verificado con `AvisoRetornoAusenciaSqlTests`). Registrado en `RegistroDependencias` y llamado desde el barrido del Worker en su propio ámbito. Nuevo `AvisoRetornoAusenciaTests` (4 casos: aviso con conteo y nombre del respaldo, no se repite, no se avisa mientras dura, sin conversaciones se sella sin mandar nada). Suite con SQL: 718/718. **Bloque 4 cerrado.** Documentación sincronizada (COR-19): el README suma «Seguimiento por tiempo» (plazos, reactivación, reingreso y multi-cuenta) y deja al día la tabla de estado; `decisiones.md` suma V37 (dos disparadores de tiempo) y retira el riesgo del reingreso, ya resuelto; `CLAUDE.md` suma la convención de elegir el disparador de las reglas por tiempo |
| [x] | T5.01 Estado de entrega (E22) | T1.07 | 2026-09-17 · `IMensajeService.ActualizarEstadoEntregaAsync` devuelve `ResultadoAcuse(MensajeId, ConversacionId, AnalistaId, PasoAFallido)` (record en Domain): `AnalistaId` es el autor o, si lo mandó el bot, quien atiende; `PasoAFallido` solo en la transición, así que la reentrega del webhook no repite el aviso (P4). Un `failed` del webhook deja `ClaseFallo.Permanente`. `RecepcionWebhook` publica `AnalistaNotificado` en la **misma transacción** del acuse, con ids y un motivo corto en palabras del analista (131047 ventana cerrada, 131026 no entregable; el resto con el texto de Meta), sin el contenido del mensaje (AL6); sin nadie a quien avisar queda en el log. `MensajeResumen` suma `Error` y `Reintentable` (texto propio, fallo permanente; no ambiguo, no transitorio, no plantilla) y Contracts suma `EstadosEntrega`. `PanelChat`: ícono por estado (○ ✓ ✓✓ ✓✓ azul ⚠) con el motivo en el tooltip, «No llegó al postulante» y «Reintentar» solo sobre mensajes propios; **desvío anotado:** «Reintentar» carga el texto en `CajaRespuesta` (`Proponer`, con clave de idempotencia nueva) en vez de enviarlo directo, para que el analista elija una plantilla si el motivo fue la ventana. Arnés: paso `Acuse`. Nuevos `E22AcuseFallidoTests` (6 casos), `NombresDeEstadoTests` (las constantes de Contracts nombran valores reales de los enums) y +5 en `MapeoBandejaTests`. Suite con SQL: 732/732. **Checklist manual pendiente:** ver los íconos y el tooltip en el chat, y usar «Reintentar» |
| [x] | T5.02 Adjuntos: modelo e intérprete | T1.03 | 2026-09-17 · `EstadoAdjunto` (Pendiente, Descargado, Rechazado, Purgado), entidad `MensajeAdjunto` con `Mensaje.Adjuntos`, tabla `MensajesAdjuntos` con la forma de 03 §ARQ-10 (FK a `Mensajes` en cascada, índice `IX_MensajesAdjuntos_Estado_FechaRecepcion`). **Desvío anotado:** suma `IntentosDescarga` y `ProximoIntentoUtc`, para que T5.04 reintente un fallo transitorio sin hacerlo para siempre (el id de medio caduca), igual que `Mensajes`; así T5.04 no necesita otra migración. `MensajeEntranteDto.Medio` (`MedioEntranteDto`: id, tipo, mime, nombre, leyenda). El intérprete lee `image`, `document`, `audio`, `video` y `sticker`: el contenido pasa a ser la leyenda o `[documento: cv.pdf]` / `[imagen]` / `[audio]`; sin id de medio se trata como tipo desconocido. `IMensajeService.RegistrarAdjuntoAsync`, llamado por `RecepcionWebhook` **dentro** de la transacción del entrante. `ReportingDbContext` (y el sembrador de sus pruebas) ignoran `Mensaje.Adjuntos`: sin eso el modelo de solo lectura dejaba de validar. Migración `AdjuntosEntrantes` revisada (solo crea la tabla y sus índices) y aplicada a `.\SQLEXPRESS`. Arnés: paso `Medio`. Pruebas: +6 en `InterpreteWebhookMetaTests` (el caso viejo `[document]` pasa a esperar el medio), nuevos `AdjuntoEntranteTests` (4: pendiente con nombre y mime, la reentrega no duplica, un texto no registra, la leyenda con el código del aviso lleva al formulario) y `AdjuntosSqlTests` (si publicar falla no queda el adjunto y la reentrega lo registra; borrar el mensaje se lo lleva). Suite con SQL: 742/742 |
| [x] | T5.03 Adjuntos: descarga del proveedor | T5.02 | 2026-09-17 · `IWhatsAppProvider.DescargarMedioAsync` con `MedioDescargado(Stream, MimeType, Tamano)` (se cierra con `using`: detrás queda la conexión abierta, porque un documento no se carga en memoria). **Desvíos anotados:** (1) devuelve `ResultadoDescarga` (medio, error y `ClaseFallo`) en vez de `MedioDescargado?`, para que T5.04 distinga reintentar de dar por perdido, igual que `ResultadoEnvio`; sin fallo ambiguo, porque un GET repetido no duplica nada. (2) 360dialog no es `GET media/{id}`: su documentación actual (Cloud API, «Upload, retrieve or delete media») pide `GET {id}` y bajar la misma ruta del CDN de Meta **en su propio host** con `D360-API-KEY`. `DescargaMedioHttp` hace los dos pasos para ambos adaptadores: 4xx en los datos del medio es permanente (id vencido, sin permiso), 408/429/5xx y cortes de red son transitorios, cualquier fallo al bajar el archivo es transitorio (la URL vale 5 minutos y el próximo intento la vuelve a pedir); Meta solo pide la URL si es https (el token no viaja sin cifrar); la URL no se escribe en el log (360dialog la trata como confidencial). No pasa por el limitador de envío. `ProveedorSimulado` entrega un PDF mínimo y registra los ids en `MediosDescargados`; `ProveedorFalso` de pruebas suma `Descarga`. Nuevo `DescargaMedioTests` (18 casos con `HandlerHttpFalso`: dos pasos con token, 404/400/401 permanentes, 5xx/429/408 transitorios, fallo al bajar, red cortada, tiempo agotado, URL sin https, respuesta sin URL, el limitador no interviene, 360dialog con su host y su clave, sin clave no sale, 360dialog caído, simulado). Suite con SQL: 760/760 |
| [x] | T5.04 Adjuntos: almacenamiento y Worker | T5.03 | 2026-09-17 · Base `AlmacenamientoArchivosLocal` extraída de `AlmacenamientoCvLocal` (cuarentena, antivirus, tope contado al copiar, borrado de parciales, `Resolver`); el CV conserva su `InvalidOperationException` y `EscaneoCvTests` sigue verde. Al extraerla se corrigió `Resolver`: comparaba la raíz sin separador final, y `cv-viejo\x` pasaba por estar «dentro» de `cv`. `AlmacenamientoAdjuntosLocal` + `OpcionesAdjuntos` (`Carpeta` vacía = `{Cv:Carpeta}/adjuntos`, `TamanoMaximoMb` 16, extensiones de documento, imagen, audio y video; antivirus de `Cv:Antivirus`): la extensión sale del nombre si lo trae o del tipo MIME, y el nombre nunca es parte de la ruta. Nueva `ArchivoRechazadoException` (Domain) para el rechazo en firme; antivirus caído o recurso compartido no disponible quedan como fallo reintentable. `IAlmacenamientoAdjuntos` + `ArchivoGuardado`; `IMensajeService` suma `ListarAdjuntosPendientesAsync`, `MarcarAdjuntoDescargadoAsync` (false si el adjunto ya no existe) y `RegistrarFalloDescargaAsync`. Caso `DescargaAdjuntos`: tiempo máximo por archivo con el reloj inyectado, espera 1-2-4-8 min hasta `IntentosMaximos`, un adjunto que falla no frena el lote, y lo guardado que no llega a marcarse se borra. `ServicioDescargaAdjuntos` con latido `DescargaAdjuntos` (sumado a `ServiciosVigilados.Todos`, así que `/health` lo vigila); **desvío anotado:** la cadencia y los reintentos van en `Worker:*` (parámetros de operación, como el despacho) y no en `ConfiguracionReglas`. `appsettings.json` de Api y Worker con la sección `Adjuntos`. No hizo falta decidir aún desde cuándo cuenta la retención: es de T5.05. Nuevos `AlmacenamientoAdjuntosTests` (16: limpio con extensión, extensión por tipo, amenaza, tope, extensión no permitida sin escanear, antivirus caído no es rechazo, carpeta propia, leer y borrar, rutas fuera de la carpeta incluida la hermana con prefijo) y `DescargaAdjuntosTests` (11: descargado, no se repite, amenaza, tope, rechazo del proveedor, reintento con espera creciente, intentos agotados, antivirus caído, lote que sigue, descarga colgada cortada, circuito con el simulado); `RegistroDependenciasTests` resuelve el caso y el almacenamiento. README («Archivos que manda el postulante por WhatsApp», estado, variables y checklist) y V33 en `decisiones.md` al día. Suite con SQL: 788/788 |
| [x] | T5.05 Adjuntos: bandeja y purga (E23) | T5.04 | 2026-09-17 · `AdjuntoResumen` y `MensajeResumen.Adjuntos` en Contracts, con `EstadosAdjunto`; `ListarPorConversacionAsync` incluye los adjuntos y `MapeoBandeja` los mapea sin la ruta, que es interna. `IMensajeService`: `ObtenerAdjuntoAsync(adjuntoId, conversacionId)` —el adjunto tiene que ser de la conversación de la ruta, si no el id solo alcanzaría para pedir el de otra—, `ListarAdjuntosPorPurgarAsync` y `MarcarAdjuntoPurgadoAsync` (auditoría `PurgaAdjunto`). `GET /conversaciones/{id}/adjuntos/{adjuntoId}` en `ConversacionesController` (mismo filtro que el chat): solo sirve lo `Descargado`, con el `Content-Type` guardado y la extensión del archivo guardado, no la del nombre que puso la persona. Nuevo caso `PurgaAdjuntos`, llamado por `ServicioPurgaCv` en el mismo ciclo diario. **Decisión fijada (V33):** el plazo `datos.retencion_adjuntos_dias` corre desde la última actividad de la persona (hilo y postulaciones), siguiendo A5, y no se purga a quien está `EnProceso`, `Reingreso` o `Contratado`. **Desvío anotado:** no hay endpoint proxy en el Frontend ni token de descarga de un solo uso; el token del analista vive solo en el circuito (V20), así que el circuito pide el archivo a la Api y se lo entrega al navegador con `DotNetStreamReference` (`wwwroot/descargas.js`), sin URL que quede en el historial. `PanelChat` muestra cada archivo con su nombre y, si no se puede bajar, por qué (revisando, rechazado, purgado). Nuevo `E23AdjuntoTests` (6: se baja desde el chat, pendiente se ve pero no se entrega, el de otra conversación no, purga con auditoría, antes del plazo no, con proceso vivo no) y `PurgaAdjuntosSqlTests` (la consulta se traduce en la base). Suite con SQL: 796/796. **Checklist manual pendiente:** mandar un PDF y una foto por WhatsApp y bajarlos desde el chat |
| [x] | T5.06 Purga de outbox | T2.12 | 2026-09-17 · `IEventoSistemaService.PurgarProcesadosAsync(dias, tamanoLote)`: borra con `ExecuteDelete` **de a lotes** —una sola sentencia sobre meses de histórico bloquearía la tabla mientras el webhook publica— y solo lo `Procesado` por `FechaProcesado`; lo pendiente y lo fallido sobreviven por viejos que sean. `ServicioPurgaCv` pasa a `ServicioMantenimientoDatos` (CVs, adjuntos y outbox en el mismo ciclo diario) **conservando el latido `PurgaCv`**, que es lo que mira el monitoreo; documentado en la clase y en el README. El aviso de una transferencia respondida deja de incluir el nombre o el teléfono del postulante (ARQ-13/AL6): la outbox no puede ser un segundo almacén fuera de la purga. Nuevo `PurgaOutboxSqlTests` (el borrado por lotes es una sentencia real y en memoria no existe) y +1 en `TransferenciaTests` que fija que ningún aviso lleva datos personales. Suite con SQL: 798/798 |
| [x] | T5.07 Anonimización extendida (E20) | T5.06, T5.05, T1.01 | 2026-09-17 · `AnonimizarDatosAsync` pasa a hacerlo todo en **una transacción** (V28) y alcanza lo que dice quién es la persona: `IConversacionService.AnonimizarPorPostulanteAsync` (teléfono → `ANON-{id}`, así que si vuelve a escribir nace un hilo nuevo con opt-in nuevo), `IMensajeService.AnonimizarPorConversacionAsync` (contenido `[anonimizado]`, sin parámetros ni opciones; adjuntos a `Purgado` sin ruta ni nombre, y devuelve las rutas para que el almacenamiento las borre), `IEventoSistemaService.AnonimizarPorConversacionAsync` y `IAuditoriaService.AnonimizarDetallesAsync`. Cada servicio toca sus tablas (V6); `PostulanteService` orquesta. **Desvío anotado:** la outbox no vacía lo `Pendiente` —es de hace segundos, su payload ya son solo identificadores (ARQ-13) y vaciarlo dejaría al consumidor con un evento ilegible—; se limpia lo procesado y lo fallido. El payload se reconoce por `"conversacionId":{id}` con su cierre (coma o llave) en camelCase y PascalCase: sin el cierre, la conversación 5 casaría con la 51. **A5:** `ListarCvsPorPurgarAsync` mide contra la última actividad de la persona y no purga con un proceso vivo, igual que los adjuntos; las pruebas que purgaban un CV con la postulación en curso se ajustaron al criterio nuevo (no se borró ninguna). Nuevo `E20AnonimizacionTests` (4) y +2 en `RetencionDatosTests`; `ServiciosDePrueba.Postulantes` arma el grafo. Suite con SQL: 804/804 |
| [x] | T5.08 Panel de alertas | T1.14 | 2026-09-17 · `AlertaOperativaResumen` en Contracts y `OperacionController`: `GET /operacion/alertas` (Jefatura y Sistemas) y `POST /operacion/alertas/{id}/resolver` (solo Sistemas, que es quien las arregla y quien responde por darlas por hechas); resolver algo que ya no está abierto devuelve 404 con motivo, no un error de sistema. `ClienteApi` suma las dos llamadas; `/configuracion` muestra la sección «Alertas operativas» con tipo, clave, detalle, contador, desde/última y «Resolver», y `NavMenu` el contador para Sistemas, que es lo que hace que alguien entre a mirar. `AutorizacionTests` declara las dos políticas nuevas y nuevo `PanelAlertasTests` (4: agrupadas con contador, resolver las saca y deja quién fue, si el problema vuelve es otra alerta, resolver una inexistente). Suite con SQL: 808/808. **Checklist manual pendiente:** ver el contador y resolver una alerta |
| [x] | T5.09 Versión de seguridad | T0.02 | 2026-09-17 · `Analista.VersionSeguridad` (`HasDefaultValue(1)`, migración `VersionSeguridadAnalista` revisada —solo agrega la columna— y aplicada a `.\SQLEXPRESS`); `IAnalistaService.ObtenerEstadoSeguridadAsync` devuelve `EstadoSeguridad(Activo, Version)`; `EstablecerContrasenaAsync` la incrementa y `ResultadoAutenticacion` la trae; el token lleva el claim `ver`. Nuevo `VerificadorSesion` (Api/Seguridad) con caché de 60 s bajo `seg:{id}`, llamado desde `OnTokenValidated`, así que alcanza también al hub, que autentica con el mismo esquema. **Desvío anotado:** la comprobación se extrajo a una clase propia en vez de quedar dentro de `Program.cs`, que no se puede probar sin levantar un host; `Program.cs` solo la llama. Nuevo `VersionSeguridadTests` (7 casos: arranca en 1, restablecer deja sin efecto el token viejo, dado de baja no entra, token sin versión, analista inexistente, la caché de 60 s y su contrapartida). El orden de ARQ-11 se resolvió solo: no hizo falta FUN-19, porque lo que incrementa la versión hoy es restablecer la contraseña y cerrar sesiones. Suite con SQL: 817/817 |
| [x] | T5.10 Cerrar sesiones | T5.09 | 2026-09-17 · `IAnalistaService.CerrarSesionesAsync` (sube la versión; false si el analista no existe) y `POST /sesion/analistas/{id}/cerrar-sesiones`, solo Sistemas. El controlador limpia además la entrada `seg:{id}` de la caché —también al restablecer una contraseña—, para que quitarle el acceso a alguien haga efecto ya y no dentro de un minuto. `ClienteApi.CerrarSesionesAsync`. **Desvío anotado:** cambiar la **propia** contraseña también sube la versión y por lo tanto cierra la sesión; `MiCuenta` lo dice y cierra la sesión en el acto, en vez de dejar que la siguiente acción falle con un «tu sesión expiró» que parecería otra cosa. +3 casos en `VersionSeguridadTests` (cerrar deja afuera el token de inmediato, cerrar a alguien inexistente da 404, restablecer desde la Api tampoco espera al minuto). Suite con SQL: 817/817 |
| [x] | T5.11 Baja y edición de analistas (E21) | T5.09, T1.13 | 2026-09-17 · `IAnalistaService.ObtenerCarteraAsync` (`ResumenCartera`) y `ActualizarAsync(id, nombre, rol, activo, autorId)` en una transacción: lo que viene nulo no se toca, y quien **deja de atender** —por la baja o por pasar a Jefatura/Sistemas— dispara `IConversacionService.ReasignarCarteraAsync` (al respaldo de la cuenta, al titular si él era el respaldo, o a «Sin clasificar» con su plazo corriendo; auditoría `ReasignadaPorBaja`), la resolución de sus transferencias pendientes (rechaza las que esperaban su respuesta, retira las que él ofreció, avisa al otro lado), la liberación de sus cuentas con alerta `CuentaSinTitular`/`CuentaSinRespaldo` y el incremento de `VersionSeguridad`. Un hilo escalado que cambia de manos vuelve a `Activa`: el escalamiento era contra quien ya no está. `PATCH /analistas/{id}` (Estructura, rechaza darse de baja a uno mismo y limpia la caché de sesión) y `GET /analistas/{id}/cartera` (Jefatura), declarados en `AutorizacionTests`; `PeticionEditarAnalista` y `CarteraAnalista` en Contracts. `Equipo.razor` suma editar, dar de baja —mostrando la cartera antes— y cerrar sesiones. Nuevo `E21BajaAnalistaTests` (7 casos) y `ServiciosDePrueba.Analistas`. Suite con SQL: 824/824. **Checklist manual pendiente:** dar de baja a un analista con conversaciones y ver a dónde van |
| [x] | T5.12 Edición de cuentas y vacantes | T3.08, T1.13 | 2026-09-17 · `ICuentaService`: `ActualizarVacanteAsync` (lo nulo no se toca; la URL tiene que ser **https** porque por el enlace viajan el DNI y el CV; el código se normaliza a mayúsculas, se valida con `CodigoAviso.EsValido` y se exige único; si queda sin formulario deja la alerta `VacanteSinFormulario`), `ReabrirVacanteAsync` y `ActualizarCuentaAsync` (desactivar deja la alerta `CuentaDesactivadaConConversaciones` si quedan hilos abiertos, y **no los mueve**: hacerlo sería decidir por el analista que los atiende). `PATCH /hc/{id}`, `PATCH /hc/{id}/reabrir` (por cuenta, declarados en `AutorizacionTests`) y `PATCH /cuentas/{id}` (Estructura). **Desvío anotado:** `GET /hc` suma `incluirCerradas`, y `ListarVacantesDeCuentaAsync` reemplaza a la lista de abiertas en la pantalla: sin ver las cerradas no hay forma de encontrar la que se cerró por error. `Vacantes.razor` suma editar, reabrir y el interruptor de cerradas; `Equipo.razor`, editar y desactivar/reactivar cuentas. Nuevo `EdicionCuentasVacantesTests` (14 casos). **Prueba ajustada:** la que esperaba rechazar un código con letras ambiguas contradecía a `CodigoAviso.EsValido`, que a propósito acepta cualquier letra o dígito cuando lo escribe una persona (FUN-20); se reescribió según esa regla y se sumó el caso que lo documenta. Suite con SQL: 838/838. **Checklist manual pendiente:** corregir un enlace de formulario y reabrir una vacante |
| [x] | T5.13 Métricas por tanda | T2.02 | 2026-09-18 · `ReportingReadModel` suma las **tandas**: una espera empieza con el primer entrante posterior a la última respuesta humana —los mensajes seguidos del postulante son una sola— y la cierra la respuesta de una persona; el bot no la cierra. Se miden en minutos hábiles con `CalendarioLaboral.MinutosHabilesEntre` y el horario de la cuenta en contexto, o el general (A15, V31). `MetricasRespuesta` suma `Tandas`, `TandasRespondidas`, `MinutosHabilesPromedio`, `MinutosHabilesMediana` y `PorcentajeDentroDelPlazo`; `ActividadAnalista`, `MinutosHabilesPromedioRespuesta`. Los campos de reloj se conservan y la pantalla los muestra al lado, porque son lo que esperó la persona en la vida real. `Metricas.razor`: la tarjeta de primera respuesta pasa a horas de atención y se suma la de «Esperas». El sembrador de las pruebas de reporting suma `HorariosAtencion` y el entorno, `ConHorarioComercial`, `Entrante` y `RespuestaDeAnalista`. +6 casos en `MetricasGerenciaTests` (dos tandas en un hilo, viernes 17:50 → lunes 09:10 = 20 minutos hábiles, mensajes seguidos = una espera, tanda sin responder, porcentaje dentro del plazo, tiempo por analista). Suite con SQL: 844/844. **Bloque 5 cerrado** |
| [x] | T6.01 Higiene y literales | B3–B5 | 2026-09-18 · Revisadas las cuatro búsquedas. (1) En `Application/Reglas` no quedan literales de negocio: los números que hay son el respaldo de `ConfigInt(clave, porDefecto)` —el valor manda desde `ConfiguracionReglas`— y las multiplicaciones `horas * 60` son conversión de unidad. (2) `CuentaService.CrearVacanteAsync` audita **después** del primer `SaveChanges`: antes el `HcId` era 0 y toda vacante creada quedaba auditada contra la entidad «0», irrastreable. (3) `AutenticacionService.HashFalso` pasa a `static readonly`: se derivaba la clave dos veces por intento fallido, 210.000 iteraciones de más justo en el camino de quien prueba contraseñas. (4) `DateTime.UtcNow` fuera del Frontend quedaba solo en `ChequeoWorker`, que ahora recibe `TimeProvider` (ARQ-01). **Desvío anotado:** los cuatro usos del Frontend (`HoraLima`, `SesionAnalista`, el rango por defecto de `/metricas`) se dejan: son presentación, no decisión de negocio, y el Frontend no tiene `TimeProvider` registrado. Suite con SQL: 844/844 |
| [x] | T6.02 Documentación final | T6.01 | 2026-09-18 · README: estructura al día (las reglas viven en Application, no en Domain), filas de estado de autenticación, administración, reporting y bandeja, y checklist de producción con el Degraded de alertas, quién las mira y las plantillas nuevas. `CLAUDE.md` suma dónde vive el orden de las reglas y qué prueba lo protege. `decisiones.md`: se cierran los pendientes ya resueltos —V1 (multi-cuenta: menú de procesos y prefijo `[Cuenta · Vacante]`, A3), V21 (vencimiento y retiro de transferencias, A1)— y V32 anota que `MenuTruncado` desapareció con el menú paginado y dónde se ven hoy las alertas. La documentación de cada bloque se fue sincronizando al cerrarlo (COR-19), así que acá solo quedaba lo que cruzaba bloques |
| [x] | T6.03 Verificación integral | T6.02 | 2026-09-18 · **(1)** `dotnet build --nologo`: 0 errores, 0 advertencias. `dotnet test` con `RRHH_PRUEBAS_SQL`: **844/844**, 0 omitidas. **(2)** Filtro de SQL Server: 24/24. **(3)** Escenarios E01–E23 presentes y en verde; E12 vive en `E11E12RepreguntaTests` y E18 en las pruebas de los adaptadores (503 → transitorio con una sola petición, respuesta perdida → ambiguo), que es donde corresponde. **(4)** Cadena completa de migraciones aplicada a una base limpia (`RRHH_Verificacion_T603`, borrada después): 24 tablas, 21 parámetros sembrados, 5 etapas de kanban y **0 plantillas activas**, como manda V8. La migración con datos de T2.04 la cubre `MigracionSeguimientoConversacionSqlTests`, que migra a la versión anterior, inserta filas y vuelve a migrar. **(5) Pendiente del usuario:** el recorrido manual con `probar-webhook-local.ps1` y `probar-jobforms-local.ps1`, y los checklists de pantalla anotados en cada tarea. **Bloque 6 cerrado** |

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
