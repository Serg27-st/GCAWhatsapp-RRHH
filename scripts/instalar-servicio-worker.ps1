<#
.SYNOPSIS
    Registra el Worker como servicio de Windows, con reinicio ante fallas (V24 y V26).

.DESCRIPTION
    Las reglas por tiempo —escalamiento de 2h, recordatorio de 24h, aviso de 48h, archivado de 90
    dias— dependen enteramente de este proceso. Como servicio arranca solo con el servidor y se
    reinicia si termina mal.

    El reinicio ante fallas no es un adorno: cuando el Worker pierde el candado de instancia unica
    termina con codigo 1 a proposito (V24), y es esta recuperacion la que lo vuelve a levantar para
    que espere su turno.

.EXAMPLE
    .\scripts\instalar-servicio-worker.ps1 -RutaPublicada D:\Publicado\RRHH\worker -Cuenta 'DOMINIO\svc_rrhh'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RutaPublicada,
    [string]$Nombre = 'RRHH WhatsApp Worker',
    [string]$Cuenta,
    [SecureString]$Contrasena
)

$ErrorActionPreference = 'Stop'

$identidad = [Security.Principal.WindowsIdentity]::GetCurrent()
$esAdmin = ([Security.Principal.WindowsPrincipal]$identidad).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $esAdmin) {
    throw 'Hay que correrlo como administrador: crear un servicio y el origen del registro de eventos lo exigen.'
}

$ejecutable = Join-Path $RutaPublicada 'RRHH.WhatsApp.Worker.exe'

if (-not (Test-Path $ejecutable)) {
    throw "No esta $ejecutable. Publica primero con publicar.ps1."
}

# El registro de eventos de Windows es donde el servicio deja lo que no puede escribir en consola.
# El origen se crea una sola vez, y crearlo exige administrador.
if (-not [System.Diagnostics.EventLog]::SourceExists($Nombre)) {
    New-EventLog -LogName Application -Source $Nombre
    Write-Host "Origen '$Nombre' creado en el registro de eventos." -ForegroundColor Cyan
}

$servicio = Get-Service -Name $Nombre -ErrorAction SilentlyContinue

if ($servicio) {
    if ($servicio.Status -ne 'Stopped') { Stop-Service -Name $Nombre -Force }

    & sc.exe config "$Nombre" binPath= "`"$ejecutable`"" start= delayed-auto | Out-Null
} else {
    & sc.exe create "$Nombre" binPath= "`"$ejecutable`"" start= delayed-auto DisplayName= "$Nombre" | Out-Null
}

if ($LASTEXITCODE -ne 0) { throw "sc.exe termino con codigo $LASTEXITCODE." }

& sc.exe description "$Nombre" 'Reglas por tiempo y cola de eventos del sistema de RRHH sobre WhatsApp.' | Out-Null

# Arranque retrasado: el Worker necesita SQL Server arriba. Si igual llega antes, la guardia del
# candado reintenta sola, pero asi se evitan errores en el log en cada reinicio del servidor.

if ($Cuenta) {
    $plano = ''

    if ($Contrasena) {
        $puntero = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Contrasena)
        try { $plano = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($puntero) }
        finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($puntero) }
    }

    & sc.exe config "$Nombre" obj= "$Cuenta" password= "$plano" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "No se pudo fijar la cuenta del servicio (codigo $LASTEXITCODE)." }

    Write-Host "Servicio corriendo como $Cuenta. Necesita permisos en SQL Server y en la carpeta de CVs." -ForegroundColor Yellow
}

# Tres reintentos con espera creciente, y el contador se reinicia al dia. failureflag 1 hace que la
# recuperacion valga tambien cuando el proceso termina con codigo distinto de cero, que es como el
# Worker avisa que perdio el candado; sin eso Windows solo reacciona a caidas.
& sc.exe failure "$Nombre" reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
& sc.exe failureflag "$Nombre" 1 | Out-Null

Start-Service -Name $Nombre

Get-Service -Name $Nombre | Format-Table Name, Status, StartType -AutoSize

Write-Host 'Listo. Las variables de entorno de maquina tienen que estar definidas antes: el servicio las lee al arrancar.'
Write-Host 'Comproba en GET /health que los bucles del Worker esten latiendo.'
