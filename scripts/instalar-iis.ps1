<#
.SYNOPSIS
    Crea o actualiza los dos sitios de IIS: la Api y la bandeja del analista (V26).

.DESCRIPTION
    Deja los pools configurados como los necesita este sistema: sin CLR administrado, sin apagado por
    inactividad y sin reciclado por tiempo. Los dos ultimos importan mas de lo que parece: cortan los
    circuitos de Blazor y el canal en vivo (Seccion 9.6.3) en medio de una conversacion.

    El binding HTTPS con el certificado de la empresa queda a mano: es la URL que ve Meta y no
    conviene armarla a ciegas desde un script.

.EXAMPLE
    .\scripts\instalar-iis.ps1 -RutaApi D:\Publicado\RRHH\api -RutaBandeja D:\Publicado\RRHH\bandeja -Cuenta 'DOMINIO\svc_rrhh'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RutaApi,
    [Parameter(Mandatory)][string]$RutaBandeja,
    [string]$SitioApi = 'RRHH.WhatsApp.Api',
    [string]$SitioBandeja = 'RRHH.WhatsApp.Bandeja',
    [int]$PuertoApi = 8081,
    [int]$PuertoBandeja = 8082,
    [string]$Cuenta,
    [SecureString]$Contrasena
)

$ErrorActionPreference = 'Stop'

$identidad = [Security.Principal.WindowsIdentity]::GetCurrent()
$esAdmin = ([Security.Principal.WindowsPrincipal]$identidad).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $esAdmin) { throw 'Hay que correrlo como administrador: crear sitios y pools de IIS lo exige.' }

Import-Module WebAdministration -ErrorAction Stop

# El modulo de ASP.NET Core lo instala el .NET Hosting Bundle. Sin el, IIS responde 500.19 y el sitio
# no arranca de ninguna forma.
$modulo = Join-Path $env:ProgramFiles 'IIS\Asp.Net Core Module\V2\aspnetcorev2.dll'

if (-not (Test-Path $modulo)) {
    throw 'Falta el .NET Hosting Bundle: instalalo y volve a correr esto (es lo que agrega el modulo de ASP.NET Core a IIS).'
}

# Blazor Server y el canal en vivo van por WebSocket. Sin la caracteristica, la bandeja cae a
# reconexiones constantes o directamente no conecta.
$websockets = Get-WindowsOptionalFeature -Online -FeatureName IIS-WebSockets -ErrorAction SilentlyContinue

if ($websockets -and $websockets.State -ne 'Enabled') {
    throw 'Falta habilitar WebSocket Protocol en IIS (Caracteristicas de Windows -> IIS -> Desarrollo de aplicaciones).'
}

function Publicar-Sitio {
    param([string]$Nombre, [string]$Ruta, [int]$Puerto)

    if (-not (Test-Path $Ruta)) { throw "No existe la carpeta publicada $Ruta." }

    if (-not (Test-Path "IIS:\AppPools\$Nombre")) { New-WebAppPool -Name $Nombre | Out-Null }

    # Sin CLR administrado: .NET Core no corre dentro del pipeline de .NET Framework.
    Set-ItemProperty "IIS:\AppPools\$Nombre" -Name managedRuntimeVersion -Value ''
    Set-ItemProperty "IIS:\AppPools\$Nombre" -Name startMode -Value 'AlwaysRunning'
    Set-ItemProperty "IIS:\AppPools\$Nombre" -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
    Set-ItemProperty "IIS:\AppPools\$Nombre" -Name recycling.periodicRestart.time -Value ([TimeSpan]::Zero)

    if ($Cuenta) {
        $plano = ''

        if ($Contrasena) {
            $puntero = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Contrasena)
            try { $plano = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($puntero) }
            finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($puntero) }
        }

        Set-ItemProperty "IIS:\AppPools\$Nombre" -Name processModel.identityType -Value 'SpecificUser'
        Set-ItemProperty "IIS:\AppPools\$Nombre" -Name processModel.userName -Value $Cuenta
        Set-ItemProperty "IIS:\AppPools\$Nombre" -Name processModel.password -Value $plano
    }

    if (-not (Test-Path "IIS:\Sites\$Nombre")) {
        New-Website -Name $Nombre -PhysicalPath $Ruta -Port $Puerto -ApplicationPool $Nombre | Out-Null
    } else {
        Set-ItemProperty "IIS:\Sites\$Nombre" -Name physicalPath -Value $Ruta
        Set-ItemProperty "IIS:\Sites\$Nombre" -Name applicationPool -Value $Nombre
    }

    # Preload: el sitio se calienta al arrancar IIS en vez de esperar a la primera visita. En la Api
    # importa porque el primero en llegar suele ser el webhook de Meta, que no espera.
    Set-ItemProperty "IIS:\Sites\$Nombre" -Name applicationDefaults.preloadEnabled -Value $true

    Restart-WebAppPool -Name $Nombre
    Start-Website -Name $Nombre

    Write-Host "$Nombre -> $Ruta (puerto $Puerto)" -ForegroundColor Green
}

Publicar-Sitio -Nombre $SitioApi -Ruta $RutaApi -Puerto $PuertoApi
Publicar-Sitio -Nombre $SitioBandeja -Ruta $RutaBandeja -Puerto $PuertoBandeja

Write-Host ''
Write-Host 'Falta a mano:' -ForegroundColor Yellow
Write-Host '  - El binding HTTPS con el certificado de la empresa sobre el sitio de la Api: es la URL que ve Meta.'
Write-Host '  - Api:BaseUrl en el appsettings.json de la bandeja, apuntando al sitio de la Api.'
Write-Host '  - Las variables de entorno de maquina (cadena de conexion, Jwt__Clave, credenciales del proveedor).'
Write-Host '  - Permisos de la cuenta del pool en SQL Server y en la carpeta de CVs.'
Write-Host ''
Write-Host 'Para comprobarlo: GET /health en verde, y entrar a la bandeja.'
