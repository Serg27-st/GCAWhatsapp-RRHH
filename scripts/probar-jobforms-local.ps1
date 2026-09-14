<#
.SYNOPSIS
    Manda al webhook del JobForms el mismo cuerpo que arma el Apps Script (V27).

.DESCRIPTION
    Sirve para recorrer el circuito del formulario sin Google: se toma el token de la invitacion
    —el que viaja en el enlace que el bot le mando al postulante— y se simula el envio.

    Repetir el mismo token dos veces comprueba la idempotencia: la segunda vez la Api responde
    yaRecibido = true y no duplica ni la respuesta ni el aviso al postulante.

.EXAMPLE
    .\scripts\probar-jobforms-local.ps1 -Token 6f1e6d0c2b5f4a1e8f0d9c3b7a2e5d41 -Dni 45678912
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Token,
    [Parameter(Mandatory)][string]$Dni,
    [string]$Nombre = 'Postulante de prueba',
    [string]$Telefono,
    [string]$Email,
    [string]$CvUrl = 'https://drive.google.com/file/d/prueba/view',
    [hashtable]$CamposOpcionales = @{},
    [string]$Url = 'http://localhost:5087/jobforms/webhook-google',
    [string]$Secreto = $env:JobForms__SecretoWebhook
)

$ErrorActionPreference = 'Stop'

if (-not $Secreto) {
    throw 'Falta el secreto compartido: pasalo con -Secreto o defini JobForms__SecretoWebhook.'
}

$envio = @{
    token                  = $Token
    dni                    = $Dni
    nombreCompleto         = $Nombre
    telefonoE164           = $Telefono
    email                  = $Email
    cvUrl                  = $CvUrl
    consentimientoAceptado = $true
    datosJson              = ($CamposOpcionales | ConvertTo-Json -Compress)
} | ConvertTo-Json -Compress

try {
    $respuesta = Invoke-WebRequest -Uri $Url -Method Post -UseBasicParsing `
        -ContentType 'application/json' `
        -Headers @{ 'X-JobForms-Secreto' = $Secreto } `
        -Body ([Text.Encoding]::UTF8.GetBytes($envio))

    "$([int]$respuesta.StatusCode) $($respuesta.Content)"
} catch {
    $fallo = $_.Exception.Response

    if (-not $fallo) { throw }

    $lector = New-Object IO.StreamReader($fallo.GetResponseStream())

    "$([int]$fallo.StatusCode) $($lector.ReadToEnd())"
}
