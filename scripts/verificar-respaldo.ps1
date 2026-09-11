<#
.SYNOPSIS
    Comprueba que las filas y los archivos de CV coincidan (Seccion 9.6.5).

.DESCRIPTION
    Detecta las dos formas en que la base y el recurso compartido pueden separarse:

      REFERENCIAS ROTAS - una fila con CvUrl que apunta a un archivo que no esta.
        El postulante cree que entrego su CV y el analista no lo encuentra. Suele venir de
        restaurar la base sin restaurar los archivos del mismo conjunto.

      ARCHIVOS HUERFANOS - un archivo que ninguna fila referencia.
        Es peor de lo que parece: la purga de la Regla 17 recorre JobFormsRespuestas, asi que
        nunca los va a encontrar. Son datos personales que sobreviven a su plazo de retencion,
        que es exactamente lo que la Ley de Proteccion de Datos Personales no permite.

    Corre contra el sistema en vivo o contra un conjunto de respaldo ya restaurado. Sirve tanto
    de chequeo periodico como de verificacion despues de restaurar.

.PARAMETER CarpetaCv
    Carpeta de los CVs a comparar.

.EXAMPLE
    .\verificar-respaldo.ps1 -CarpetaCv \servidor\rrhh\cv
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CarpetaCv,
    [string]$Servidor  = '.\SQLEXPRESS',
    [string]$BaseDatos = 'RRHH_WhatsApp'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $CarpetaCv)) { throw "No existe la carpeta de CVs: $CarpetaCv" }

# Solo las rutas relativas. Un CvUrl que empieza con http vive en Google Drive y no en el recurso
# compartido: no se puede verificar desde aca, y contarlo como roto seria una falsa alarma.
$consulta = @"
SET NOCOUNT ON;
SELECT CvUrl FROM JobFormsRespuestas
WHERE CvUrl IS NOT NULL AND CvUrl NOT LIKE 'http%';
"@

$enBase = @(sqlcmd -S $Servidor -d $BaseDatos -E -C -h -1 -W -Q $consulta |
            Where-Object { $_ -and $_ -notmatch '^\(' } |
            ForEach-Object { $_.Trim() })

$raiz = (Resolve-Path $CarpetaCv).Path.TrimEnd('\')

# _cuarentena guarda archivos que el antivirus todavia esta revisando: no son CVs y no tienen
# fila que los referencie, asi que contarlos como huerfanos seria una falsa alarma constante.
$enDisco = @(Get-ChildItem -Path $raiz -Recurse -File -ErrorAction SilentlyContinue |
             Where-Object { $_.FullName -notmatch [regex]::Escape("\_cuarentena\") } |
             ForEach-Object { $_.FullName.Substring($raiz.Length + 1) })

# Windows no distingue mayusculas en rutas; comparar con distincion daria falsos positivos.
$comparador = [System.StringComparer]::OrdinalIgnoreCase

$setDisco = [System.Collections.Generic.HashSet[string]]::new([string[]]$enDisco, $comparador)
$setBase  = [System.Collections.Generic.HashSet[string]]::new([string[]]$enBase,  $comparador)

$rotas     = @($enBase  | Where-Object { -not $setDisco.Contains($_) })
$huerfanos = @($enDisco | Where-Object { -not $setBase.Contains($_) })

Write-Host "Filas con CV local : $($enBase.Count)"
Write-Host "Archivos en disco  : $($enDisco.Count)"
Write-Host ""

if ($rotas.Count -eq 0 -and $huerfanos.Count -eq 0) {
    Write-Host "Todo coincide." -ForegroundColor Green
    exit 0
}

if ($rotas.Count -gt 0) {
    Write-Host "REFERENCIAS ROTAS ($($rotas.Count)) - la fila existe, el archivo no:" -ForegroundColor Red
    $rotas | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" }
    if ($rotas.Count -gt 20) { Write-Host "  ... y $($rotas.Count - 20) mas" }
    Write-Host ""
}

if ($huerfanos.Count -gt 0) {
    Write-Host "ARCHIVOS HUERFANOS ($($huerfanos.Count)) - la purga de la Regla 17 nunca los vera:" -ForegroundColor Yellow
    $huerfanos | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" }
    if ($huerfanos.Count -gt 20) { Write-Host "  ... y $($huerfanos.Count - 20) mas" }
    Write-Host ""
}

# Codigo distinto de cero para que una tarea programada lo pueda detectar sin leer la salida.
exit 1
