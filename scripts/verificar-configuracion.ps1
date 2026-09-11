<#
.SYNOPSIS
    Revisa que el entorno este listo para levantar el sistema, y dice que falta.

.DESCRIPTION
    Comprueba, en el orden en que las cosas fallan:

      1. Las variables del proveedor, y cual quedaria elegido. Es el chequeo que mas tiempo ahorra:
         setx no afecta a las consolas ya abiertas, asi que la causa mas comun de "configure todo y
         sigue simulado" es haber levantado la Api desde una consola vieja. Este script lee las
         variables del mismo modo que lo hara dotnet run, asi que si aca aparecen, la Api las vera.

      2. La cadena de conexion y el estado de las migraciones.

      3. Los datos de operacion. La migracion siembra etapas, parametros y plantillas, pero no
         cuentas ni analistas: sin una cuenta con titular y una vacante abierta el bot no tiene que
         ofrecer y la Regla 1 no tiene a quien asignar la conversacion.

    NUNCA imprime el valor de un secreto, solo si esta definido. Un script de diagnostico que
    vuelca credenciales a la consola termina pegado en un chat o en un ticket.

.EXAMPLE
    .\verificar-configuracion.ps1
#>
[CmdletBinding()]
param(
    [string]$Servidor  = '.\SQLEXPRESS',
    [string]$BaseDatos = 'RRHH_WhatsApp'
)

$ErrorActionPreference = 'Continue'
$problemas = New-Object System.Collections.Generic.List[string]

function Escribir-Item([bool]$ok, [string]$texto, [string]$detalle) {
    if ($ok) {
        Write-Host "  [ok]    $texto" -ForegroundColor Green
    }
    else {
        Write-Host "  [falta] $texto" -ForegroundColor Yellow
        if ($detalle) { Write-Host "          $detalle" -ForegroundColor DarkGray }
    }
}

Write-Host ''
Write-Host 'PROVEEDOR DE WHATSAPP' -ForegroundColor Cyan

$phoneId   = $env:MetaCloud__PhoneNumberId
$token     = $env:MetaCloud__AccessToken
$appSecret = $env:MetaCloud__AppSecret
$verif     = $env:MetaCloud__TokenVerificacion
$apiKey360 = $env:Dialog360__ApiKey

$tienePhoneId   = -not [string]::IsNullOrWhiteSpace($phoneId)
$tieneToken     = -not [string]::IsNullOrWhiteSpace($token)
$tieneAppSecret = -not [string]::IsNullOrWhiteSpace($appSecret)
$tieneVerif     = -not [string]::IsNullOrWhiteSpace($verif)

Escribir-Item $tienePhoneId   'MetaCloud__PhoneNumberId'    'WhatsApp > Configuracion de la API. Es el id, no el numero.'
Escribir-Item $tieneToken     'MetaCloud__AccessToken'      'La misma pantalla. El temporal vence a las 24 horas.'
Escribir-Item $tieneAppSecret 'MetaCloud__AppSecret'        'Configuracion > Basica. Sin esto el webhook rechaza TODO lo entrante.'
Escribir-Item $tieneVerif     'MetaCloud__TokenVerificacion' 'La inventas vos y la repetis en Meta al dar de alta el webhook.'

Write-Host ''

# Mismo orden que RegistroDependencias.AgregarProveedorWhatsApp: Meta, 360dialog, simulado.
if ($tieneToken -and $tienePhoneId) {
    Write-Host '  Proveedor elegido: Meta Cloud API' -ForegroundColor Green

    if (-not $tieneAppSecret) {
        $problemas.Add('Vas a poder ENVIAR pero no RECIBIR: sin AppSecret el webhook rechaza todo.')
    }
    if (-not $tieneVerif) {
        $problemas.Add('Sin TokenVerificacion Meta no te deja registrar el webhook (da 403).')
    }
}
elseif (-not [string]::IsNullOrWhiteSpace($apiKey360)) {
    Write-Host '  Proveedor elegido: 360dialog' -ForegroundColor Green
}
else {
    Write-Host '  Proveedor elegido: SIMULADO' -ForegroundColor Yellow
    Write-Host '  Los envios quedan en el log y no salen a la red.' -ForegroundColor DarkGray
    $problemas.Add('Faltan AccessToken y PhoneNumberId: el sistema no va a hablar con WhatsApp real.')
    $problemas.Add('Si ya corriste setx, cerra esta consola y abri una nueva: setx no afecta a las abiertas.')
}

Write-Host ''
Write-Host 'BASE DE DATOS' -ForegroundColor Cyan

$migraciones = @()
$baseViva = $false

try {
    $salida = sqlcmd -S $Servidor -d $BaseDatos -E -C -h -1 -W `
        -Q "SET NOCOUNT ON; SELECT MigrationId FROM __EFMigrationsHistory;" 2>&1

    if ($LASTEXITCODE -eq 0) {
        $baseViva = $true
        $migraciones = @($salida | Where-Object { $_ -match '^\d{14}_' })
    }
}
catch { }

Escribir-Item $baseViva "Conexion a $Servidor / $BaseDatos" 'Revisa que el servicio SQL Server (SQLEXPRESS) este corriendo.'

if ($baseViva) {
    Escribir-Item ($migraciones.Count -gt 0) "Migraciones aplicadas: $($migraciones.Count)" `
        'Corre: dotnet ef database update --project src/RRHH.WhatsApp.Infrastructure'

    if ($migraciones.Count -eq 0) { $problemas.Add('La base existe pero no tiene el esquema aplicado.') }
}
else {
    $problemas.Add('Sin base de datos no arranca nada.')
}

if (-not $baseViva) {
    Write-Host ''
    Write-Host 'No se puede revisar la operacion sin base.' -ForegroundColor DarkGray
}
else {
    Write-Host ''
    Write-Host 'DATOS DE OPERACION' -ForegroundColor Cyan

    $consulta = @"
SET NOCOUNT ON;
SELECT
  (SELECT COUNT(*) FROM Cuentas c
     WHERE EXISTS (SELECT 1 FROM AnalistaCuenta ac WHERE ac.CuentaId=c.CuentaId AND ac.EsBackup=0)
       AND EXISTS (SELECT 1 FROM HC h WHERE h.CuentaId=c.CuentaId AND h.Estado=1)),
  (SELECT COUNT(*) FROM Analistas),
  (SELECT COUNT(*) FROM Analistas WHERE HashContrasena IS NOT NULL),
  (SELECT COUNT(*) FROM HC WHERE Estado=1 AND (UrlJobForms IS NULL OR UrlJobForms='')),
  (SELECT COUNT(*) FROM Plantillas WHERE Activa=1),
  (SELECT COUNT(*) FROM Plantillas)
"@

    $fila = (sqlcmd -S $Servidor -d $BaseDatos -E -C -h -1 -W -s'|' -Q $consulta 2>&1 |
        Where-Object { $_ -match '\|' } | Select-Object -First 1)

    $n = $fila -split '\|'

    if ($n.Count -ge 6) {
        $cuentasListas   = [int]$n[0]
        $analistas       = [int]$n[1]
        $conContrasena   = [int]$n[2]
        $hcSinForm       = [int]$n[3]
        $plantillasOn    = [int]$n[4]
        $plantillasTotal = [int]$n[5]

        Escribir-Item ($cuentasListas -gt 0) "Cuentas con titular y vacante abierta: $cuentasListas" `
            'Sin al menos una, el menu del bot sale vacio. Ver "Dejar la operacion en condiciones" en el README.'

        Escribir-Item ($analistas -gt 0) "Analistas dados de alta: $analistas" `
            'POST /analistas'

        Escribir-Item ($conContrasena -gt 0) "Analistas que pueden entrar a la bandeja: $conContrasena" `
            'POST /sesion/arranque fija la primera contrasena y se cierra sola.'

        if ($cuentasListas -eq 0) { $problemas.Add('Ninguna cuenta esta en condiciones de recibir postulantes.') }
        if ($conContrasena -eq 0) { $problemas.Add('Nadie puede entrar a la bandeja todavia.') }

        if ($hcSinForm -gt 0) {
            Write-Host "  [aviso] $hcSinForm vacante(s) abiertas sin UrlJobForms" -ForegroundColor Yellow
            Write-Host '          El bot no manda el enlace y queda un evento VacanteSinFormulario.' -ForegroundColor DarkGray
        }

        # Las 6 nacen inactivas a proposito (desviacion V8): se activan recien tras la aprobacion
        # de Meta. Que esten en cero no es un error, es el estado correcto antes de produccion.
        Write-Host "  [info]  Plantillas activas: $plantillasOn de $plantillasTotal" -ForegroundColor DarkGray
        Write-Host '          Nacen inactivas a proposito. Se activan recien tras aprobarlas Meta.' -ForegroundColor DarkGray
    }
}

Write-Host ''
Write-Host 'HERRAMIENTAS' -ForegroundColor Cyan

$tunel = Get-Command cloudflared -ErrorAction SilentlyContinue
Escribir-Item ($null -ne $tunel) 'cloudflared (tunel para el webhook)' `
    'Sin tunel Meta no alcanza tu maquina. Cualquier alternativa sirve (ngrok, devtunnel).'

Write-Host ''

if ($problemas.Count -eq 0) {
    Write-Host 'Todo listo.' -ForegroundColor Green
    Write-Host ''
    exit 0
}

Write-Host 'PENDIENTE' -ForegroundColor Yellow
foreach ($p in $problemas) { Write-Host "  - $p" }
Write-Host ''
exit 1
