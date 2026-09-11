<#
.SYNOPSIS
    Restaura un conjunto de respaldo completo: base y CVs juntos (Seccion 9.6.5).

.DESCRIPTION
    Restaura los dos lados desde EL MISMO conjunto. Mezclar una base de hoy con archivos de la
    semana pasada es justamente la inconsistencia que el respaldo coordinado evita, y es facil de
    hacer sin darse cuenta cuando cada cosa se restaura por su lado.

    Al terminar corre la verificacion, porque una restauracion que nadie comprueba es una
    suposicion.

    DESTRUCTIVO. Reemplaza la base indicada y espeja la carpeta de CVs. Pide confirmacion salvo
    que se pase -Forzar.

.PARAMETER Conjunto
    Carpeta del conjunto a restaurar, la que contiene manifiesto.json.

.PARAMETER CarpetaCv
    Carpeta de CVs destino. Se espeja: lo que no este en el respaldo se borra.

.EXAMPLE
    .\restaurar.ps1 -Conjunto D:\Respaldos\RRHH\20260824-020000 -CarpetaCv \servidor\rrhh\cv
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Conjunto,
    [string]$Servidor   = '.\SQLEXPRESS',
    [string]$BaseDatos  = 'RRHH_WhatsApp',
    [Parameter(Mandatory)][string]$CarpetaCv,
    [switch]$Forzar
)

$ErrorActionPreference = 'Stop'

$rutaManifiesto = Join-Path $Conjunto 'manifiesto.json'

if (-not (Test-Path $rutaManifiesto)) {
    throw "No hay manifiesto.json en $Conjunto. Sin el no se puede saber que par restaurar."
}

$m = Get-Content $rutaManifiesto -Raw | ConvertFrom-Json

Write-Host "Conjunto  : $($m.sello)"
Write-Host "Tomado    : $($m.fechaUtc) UTC"
Write-Host "Contiene  : $($m.archivosCv) CV(s), $($m.filasConCv) fila(s) con CV"
Write-Host ""
Write-Host "Se va a REEMPLAZAR:" -ForegroundColor Yellow
Write-Host "  base   $BaseDatos en $Servidor"
Write-Host "  cv     $CarpetaCv (se espeja: lo que sobre se borra)"
Write-Host ""

if (-not $Forzar) {
    $r = Read-Host "Escriba SI para continuar"
    if ($r -ne 'SI') { Write-Host "Cancelado."; exit 0 }
}

$rutaBak = $m.rutaBak

if (-not (Test-Path $rutaBak)) {
    # El conjunto pudo haberse movido de servidor desde que se tomo.
    $rutaBak = Join-Path $Conjunto "$BaseDatos.bak"
}

if (-not (Test-Path $rutaBak)) { throw "No se encuentra el .bak del conjunto." }

# --- 1. La base ------------------------------------------------------------------------------
Write-Host "[1/3] Restaurando la base ..."

# SINGLE_USER corta las conexiones abiertas: sin esto el RESTORE falla si la Api o el Worker
# siguen conectados. Conviene detenerlos antes igual.
$sql = @"
ALTER DATABASE [$BaseDatos] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
RESTORE DATABASE [$BaseDatos] FROM DISK = N'$rutaBak' WITH REPLACE, STATS = 25;
ALTER DATABASE [$BaseDatos] SET MULTI_USER;
"@

sqlcmd -S $Servidor -E -C -b -Q $sql
if ($LASTEXITCODE -ne 0) { throw "Fallo la restauracion de la base." }

# --- 2. Los CVs ------------------------------------------------------------------------------
Write-Host "[2/3] Restaurando los CVs ..."

$origenCv = Join-Path $Conjunto 'cv'

if (-not (Test-Path $origenCv)) { throw "El conjunto no trae carpeta cv." }

New-Item -ItemType Directory -Force -Path $CarpetaCv | Out-Null

robocopy $origenCv $CarpetaCv /MIR /R:2 /W:2 /NP /NFL /NDL | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Fallo la restauracion de los CVs (robocopy $LASTEXITCODE)." }

# --- 3. Comprobar ----------------------------------------------------------------------------
Write-Host "[3/3] Verificando que base y archivos coincidan ..."
Write-Host ""

& (Join-Path $PSScriptRoot 'verificar-respaldo.ps1') `
    -CarpetaCv $CarpetaCv -Servidor $Servidor -BaseDatos $BaseDatos
