<#
.SYNOPSIS
    Publica los tres procesos del sistema para el despliegue on-premise (V26).

.DESCRIPTION
    Deja en $Destino una carpeta por proceso: api y bandeja para los sitios de IIS, worker para el
    servicio de Windows. Publica framework-dependent a proposito: el servidor necesita el .NET
    Hosting Bundle igual, porque es lo que instala el modulo de ASP.NET Core en IIS.

    Los secretos no se publican. Viajan como variables de entorno de maquina (ver el README).

.EXAMPLE
    .\scripts\publicar.ps1 -Destino D:\Publicado\RRHH
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Destino,
    [string]$Configuracion = 'Release'
)

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot

$procesos = @(
    @{ Carpeta = 'api';     Proyecto = 'src\RRHH.WhatsApp.Api';      EnIis = $true },
    @{ Carpeta = 'bandeja'; Proyecto = 'src\RRHH.WhatsApp.Frontend'; EnIis = $true },
    @{ Carpeta = 'worker';  Proyecto = 'src\RRHH.WhatsApp.Worker';   EnIis = $false }
)

foreach ($proceso in $procesos) {
    $salida = Join-Path $Destino $proceso.Carpeta

    Write-Host "Publicando $($proceso.Carpeta)..." -ForegroundColor Cyan

    dotnet publish (Join-Path $raiz $proceso.Proyecto) -c $Configuracion -o $salida --nologo
    if ($LASTEXITCODE -ne 0) { throw "Fallo la publicacion de $($proceso.Carpeta)." }

    # La configuracion de desarrollo no tiene nada que hacer en el servidor: apunta a la base local
    # y deja el ambiente en Development, que muestra la pagina de errores con el detalle interno.
    $desarrollo = Join-Path $salida 'appsettings.Development.json'
    if (Test-Path $desarrollo) { Remove-Item $desarrollo }

    # Sin web.config, IIS no sabe como arrancar el sitio y responde 500.19. Lo genera el publish:
    # si falta, algo salio mal y conviene enterarse aca y no en el servidor.
    if ($proceso.EnIis -and -not (Test-Path (Join-Path $salida 'web.config'))) {
        throw "La publicacion de $($proceso.Carpeta) no genero web.config."
    }
}

Write-Host ''
Write-Host "Publicado en $Destino" -ForegroundColor Green
Write-Host 'Sigue: instalar-iis.ps1 para los sitios, e instalar-servicio-worker.ps1 para el Worker.'
Write-Host 'Los secretos van por variables de entorno de maquina, no en estos archivos.'
