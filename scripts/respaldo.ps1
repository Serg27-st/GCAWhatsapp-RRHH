<#
.SYNOPSIS
    Respaldo coordinado de la base y de los CVs (Seccion 9.6.5 del dossier).

.DESCRIPTION
    Los datos del postulante viven en dos lugares: las filas en SQL Server y los archivos en el
    recurso compartido. Respaldarlos por separado deja restauraciones inconsistentes, que es lo
    que este script evita.

    EL ORDEN IMPORTA. Primero la base, despues los archivos.

    El circuito del JobForms escribe el CV al disco ANTES de confirmar la fila que lo referencia
    (ver JobFormsController.Enviar). Entonces:

      - Base primero: toda fila del respaldo ya tiene su archivo en disco, y la copia posterior
        lo captura. Ninguna referencia queda rota.
      - Archivos primero: una fila insertada entre ambos pasos apuntaria a un archivo que el
        respaldo no tiene. Referencia rota, y un CV que el postulante cree entregado.

    Al terminar deja un manifiesto que empareja los dos respaldos. La restauracion se apoya en el
    para no mezclar una base de hoy con archivos de la semana pasada.

.PARAMETER Destino
    Carpeta raiz donde se acumulan los conjuntos de respaldo.

.PARAMETER Servidor
    Instancia de SQL Server.

.PARAMETER BaseDatos
    Nombre de la base.

.PARAMETER CarpetaCv
    Carpeta de los CVs: el valor de Cv:Carpeta del appsettings del ambiente.

.PARAMETER Conservar
    Cuantos conjuntos completos mantener. Los mas viejos se borran al final.

.EXAMPLE
    .\respaldo.ps1 -Destino D:\Respaldos\RRHH -CarpetaCv \servidor\rrhh\cv
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Destino,
    [string]$Servidor   = '.\SQLEXPRESS',
    [string]$BaseDatos  = 'RRHH_WhatsApp',
    [Parameter(Mandatory)][string]$CarpetaCv,
    [int]$Conservar     = 14
)

$ErrorActionPreference = 'Stop'

# Un unico sello para los dos respaldos: es lo que permite emparejarlos despues.
$sello    = Get-Date -Format 'yyyyMMdd-HHmmss'
$conjunto = Join-Path $Destino $sello

New-Item -ItemType Directory -Force -Path $conjunto | Out-Null

Write-Host "Conjunto de respaldo: $conjunto"

# --- 1. La base, primero ---------------------------------------------------------------------
$rutaBak = Join-Path $conjunto "$BaseDatos.bak"

# Express no soporta compresion de respaldo, y el despliegue de este proyecto es sobre Express.
# Se consulta en vez de asumir, para que el mismo script sirva si algun dia se pasa a Standard.
$edicion = (sqlcmd -S $Servidor -E -C -h -1 -W -Q "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('Edition') AS nvarchar(128));" |
            Select-Object -First 1)

$compresion = if ($edicion -match 'Express') { '' } else { 'COMPRESSION, ' }

if ($compresion -eq '') { Write-Host "  ($($edicion.Trim()): sin compresion)" }

Write-Host "[1/3] Respaldando $BaseDatos ..."

$sql = @"
BACKUP DATABASE [$BaseDatos]
TO DISK = N'$rutaBak'
WITH FORMAT, INIT, ${compresion}CHECKSUM, STATS = 25,
     NAME = N'$BaseDatos-$sello';
"@

sqlcmd -S $Servidor -E -C -b -Q $sql

if ($LASTEXITCODE -ne 0) {
    throw @"
Fallo el respaldo de la base.

Si el error dice 'Cannot open backup device ... error 5 (Acceso denegado)', no es un problema del
script: BACKUP corre como la cuenta del servicio de SQL Server, no como quien ejecuta esto. Hay que
darle permiso de escritura sobre '$Destino'. La cuenta se averigua con:

  SELECT service_account FROM sys.dm_server_services WHERE servicename LIKE 'SQL Server (%';

y el permiso se otorga con:

  icacls "$Destino" /grant "<cuenta>:(OI)(CI)M"
"@
}

# Verificar aca y no al restaurar: un .bak corrupto descubierto el dia de la restauracion ya no
# sirve de nada.
Write-Host "[2/3] Verificando el respaldo ..."

sqlcmd -S $Servidor -E -C -b -Q "RESTORE VERIFYONLY FROM DISK = N'$rutaBak';"
if ($LASTEXITCODE -ne 0) { throw "El respaldo de la base no paso RESTORE VERIFYONLY." }

# --- 2. Los CVs, despues ---------------------------------------------------------------------
$destinoCv = Join-Path $conjunto 'cv'

Write-Host "[3/3] Copiando los CVs desde $CarpetaCv ..."

if (-not (Test-Path $CarpetaCv)) { throw "No existe la carpeta de CVs: $CarpetaCv" }

# /MIR espeja, /R:2 no se queda reintentando un archivo bloqueado, /NP sin barra de progreso.
# _cuarentena queda fuera: lo que hay ahi son archivos a medio revisar, no CVs.
robocopy $CarpetaCv $destinoCv /MIR /XD _cuarentena /R:2 /W:2 /NP /NFL /NDL | Out-Null

# Robocopy usa 0-7 para exito (8 en adelante es fallo real); no se puede comparar contra 0.
if ($LASTEXITCODE -ge 8) { throw "Fallo la copia de los CVs (robocopy $LASTEXITCODE)." }

# --- 3. Manifiesto ---------------------------------------------------------------------------
$archivos = @(Get-ChildItem -Path $destinoCv -Recurse -File -ErrorAction SilentlyContinue)

$filas = sqlcmd -S $Servidor -d $BaseDatos -E -C -h -1 -W `
    -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM JobFormsRespuestas WHERE CvUrl IS NOT NULL;"

$manifiesto = [ordered]@{
    sello              = $sello
    fechaUtc           = (Get-Date).ToUniversalTime().ToString('o')
    servidor           = $Servidor
    baseDatos          = $BaseDatos
    rutaBak            = $rutaBak
    carpetaCvOrigen    = $CarpetaCv
    carpetaCvRespaldo  = $destinoCv
    archivosCv         = $archivos.Count
    bytesCv            = ($archivos | Measure-Object -Property Length -Sum).Sum
    filasConCv         = [int]($filas | Select-Object -First 1).Trim()
    orden              = 'base -> cv (el archivo se escribe antes que su fila)'
}

$manifiesto | ConvertTo-Json | Out-File (Join-Path $conjunto 'manifiesto.json') -Encoding utf8

Write-Host ""
Write-Host "Listo. $($manifiesto.archivosCv) CV(s), $($manifiesto.filasConCv) fila(s) con CV."

# --- 4. Retencion ----------------------------------------------------------------------------
# Se borra al final y solo si todo lo anterior salio bien: perder el respaldo viejo antes de
# tener el nuevo confirmado deja a la empresa sin ninguno.
$viejos = Get-ChildItem -Path $Destino -Directory |
          Where-Object { $_.Name -match '^\d{8}-\d{6}$' } |
          Sort-Object Name -Descending |
          Select-Object -Skip $Conservar

foreach ($v in $viejos) {
    Write-Host "Purgando conjunto viejo: $($v.Name)"
    Remove-Item $v.FullName -Recurse -Force
}
