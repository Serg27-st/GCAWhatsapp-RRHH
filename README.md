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

## Estado

| Componente | Estado |
|---|---|
| Modelo de datos + migración inicial | listo — 20 tablas, semillas de etapas kanban, parámetros y plantillas |
| Contratos de dominio (Sección 9.3) | listo |
| Motor de reglas | listo |
| Reglas implementadas | 1, 2, 3, 14, 15, 19 |
| Reglas pendientes | 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 16, 17, 18, 20 |
| Adaptador 360dialog | pendiente |
| Webhook + endpoints | pendiente |
| Worker | pendiente |
| Bandeja del analista | pendiente |

## Antes de salir a producción

- [ ] WABA verificado y aprobado por Meta
- [ ] Las 6 plantillas de `Plantillas` registradas y aprobadas en Meta, y marcadas `Activa = true`
- [ ] URL pública HTTPS con certificado válido para el webhook
- [ ] Cadena de conexión y credenciales de 360dialog fuera del código
- [ ] Cuentas, analistas, respaldos por cuenta y horario de atención cargados
- [ ] Aviso de privacidad revisado por Legal y su versión registrada en `ConfiguracionReglas`
- [ ] Backups coordinados entre SQL Server y el almacenamiento de CVs
