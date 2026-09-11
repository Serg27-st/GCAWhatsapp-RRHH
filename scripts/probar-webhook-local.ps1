<#
.SYNOPSIS
    Simula una entrega del webhook de WhatsApp, firmada como la firma Meta.

.DESCRIPTION
    Sirve para probar el circuito completo -firma, idempotencia, opt-in, outbox, motor de reglas y
    envio- sin tunel, sin numero de prueba y sin esperar la aprobacion del WABA.

    El payload tiene la forma exacta de la Cloud API de Meta, que es la que 360dialog reenvia, y se
    firma con HMAC-SHA256 sobre los bytes crudos, igual que lo hace Meta. Se mandan las dos
    cabeceras de firma -la de Meta y la de 360dialog- para que el script funcione con cualquiera de
    los dos proveedores configurados, sin tener que saber cual esta activo.

    Que esperar despues de correrlo:
      - Log de la Api    : "Webhook procesado: 1 nuevos"
      - Log del Worker   : el motor evaluando las reglas
      - Bandeja          : la conversacion con su hilo

    Si el Worker esta caido el mensaje se guarda igual, pero nadie lo responde: la respuesta del
    script dira 1 nuevo y no pasara nada mas.

.PARAMETER Texto
    Cuerpo del mensaje. Con -Boton pasa a ser el titulo del boton pulsado.

.PARAMETER Boton
    Id de la opcion pulsada. Con esto el mensaje va como interactive/button_reply en vez de texto,
    que es lo que la Regla 19 necesita para distinguir una opcion valida del texto libre.

.PARAMETER Wamid
    Id del mensaje. Por defecto se genera uno nuevo en cada corrida. Repetir uno a proposito es la
    forma de comprobar la idempotencia: la segunda entrega debe contarse como duplicada y no
    reabrir la ventana de 24h.

.EXAMPLE
    .\probar-webhook-local.ps1
    Un "Hola" de un numero nuevo. Deberia disparar el menu de empresas (Regla 19).

.EXAMPLE
    .\probar-webhook-local.ps1 -Boton 'cuenta_1' -Texto 'Alicorp'
    Simula que el postulante eligio una empresa del menu.

.EXAMPLE
    .\probar-webhook-local.ps1 -Wamid 'wamid.FIJO1'
    .\probar-webhook-local.ps1 -Wamid 'wamid.FIJO1'
    La segunda corrida debe responder duplicados=1 y nuevos=0.
#>
[CmdletBinding()]
param(
    [string]$Telefono = '+51987654321',
    [string]$Nombre   = 'Maria Quispe',
    [string]$Texto    = 'Hola, vi el aviso de trabajo',
    [string]$Boton,
    [string]$Wamid,
    [string]$Url      = 'http://localhost:5087/webhook/whatsapp'
)

$ErrorActionPreference = 'Stop'

# El webhook rechaza todo si no puede verificar la firma, asi que el secreto no es opcional ni
# aca. Se toma el del proveedor que este configurado, en el mismo orden en que lo hace
# RegistroDependencias: Meta primero, 360dialog despues.
$secreto = $env:MetaCloud__AppSecret
if ([string]::IsNullOrWhiteSpace($secreto)) { $secreto = $env:Dialog360__SecretoWebhook }

if ([string]::IsNullOrWhiteSpace($secreto)) {
    throw @'
No hay secreto de webhook configurado.

Defini MetaCloud__AppSecret (clave secreta de la app, en Meta > Configuracion > Basica) o
Dialog360__SecretoWebhook, y abri una consola nueva -setx no afecta a las ya abiertas-.

Sin secreto el webhook rechaza todo, que es el comportamiento buscado: preferimos no recibir nada
antes que aceptar un payload que no podemos atribuir.
'@
}

if ([string]::IsNullOrWhiteSpace($Wamid)) {
    $Wamid = 'wamid.PRUEBA' + [Guid]::NewGuid().ToString('N').Substring(0, 16).ToUpperInvariant()
}

# Meta entrega el numero sin el signo; el dominio lo normaliza a E.164 al interpretarlo.
$waId = ($Telefono -replace '\D', '')
$marca = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

function ConvertTo-JsonTexto([string]$valor) {
    if ($null -eq $valor) { return '' }
    return ($valor -replace '\\', '\\' -replace '"', '\"')
}

$textoJson   = ConvertTo-JsonTexto $Texto
$nombreJson  = ConvertTo-JsonTexto $Nombre

if ([string]::IsNullOrWhiteSpace($Boton)) {
    $mensajeJson = @"
              "type": "text",
              "text": { "body": "$textoJson" }
"@
}
else {
    $botonJson = ConvertTo-JsonTexto $Boton
    $mensajeJson = @"
              "type": "interactive",
              "interactive": {
                "type": "button_reply",
                "button_reply": { "id": "$botonJson", "title": "$textoJson" }
              }
"@
}

$payload = @"
{
  "object": "whatsapp_business_account",
  "entry": [{
    "id": "102290129340398",
    "changes": [{
      "field": "messages",
      "value": {
        "messaging_product": "whatsapp",
        "metadata": { "display_phone_number": "51999888777", "phone_number_id": "1234" },
        "contacts": [{ "profile": { "name": "$nombreJson" }, "wa_id": "$waId" }],
        "messages": [{
          "from": "$waId",
          "id": "$Wamid",
          "timestamp": "$marca",
$mensajeJson
        }]
      }
    }]
  }]
}
"@

# La firma se calcula sobre los bytes exactos que van a viajar. Si se dejara que Invoke-RestMethod
# serialice un objeto, el cuerpo que llega no seria byte a byte el que se firmo y la validacion
# fallaria por una razon que no tiene nada que ver con el secreto.
$bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)

$hmac = New-Object System.Security.Cryptography.HMACSHA256
$hmac.Key = [System.Text.Encoding]::UTF8.GetBytes($secreto)
$firma = ((($hmac.ComputeHash($bytes)) | ForEach-Object { $_.ToString('x2') }) -join '')
$hmac.Dispose()

# Cada cabecera en el formato que manda su proveedor: Meta antepone el algoritmo y lo exige,
# 360dialog manda el hex pelado. El adaptador de 360dialog tolera el prefijo, pero el proveedor
# simulado no, asi que prefijar las dos dejaria el script sin funcionar justo en desarrollo.
$cabeceras = @{
    'X-Hub-Signature-256'   = "sha256=$firma"
    'x-360dialog-signature' = $firma
}

Write-Host "POST $Url" -ForegroundColor Cyan
Write-Host "  de      : $Telefono ($Nombre)"
Write-Host "  wamid   : $Wamid"
if ([string]::IsNullOrWhiteSpace($Boton)) {
    Write-Host "  texto   : $Texto"
}
else {
    Write-Host "  boton   : $Boton ($Texto)"
}
Write-Host ''

try {
    $respuesta = Invoke-RestMethod -Uri $Url -Method Post -Body $bytes `
        -ContentType 'application/json' -Headers $cabeceras
}
catch {
    $codigo = $null
    if ($_.Exception.Response) { $codigo = [int]$_.Exception.Response.StatusCode }

    if ($codigo -eq 401) {
        throw @'
401: la Api rechazo la firma.

El secreto de este script y el que tiene la Api no coinciden. Suele ser que la Api se levanto en
una consola anterior al setx: cerrala y volve a levantarla desde una consola nueva.
'@
    }

    throw "Fallo la llamada al webhook ($codigo): $($_.Exception.Message)"
}

Write-Host ("nuevos={0}  duplicados={1}  acuses={2}" -f `
    $respuesta.recibidos, $respuesta.duplicados, $respuesta.acuses) -ForegroundColor Green

if ($respuesta.recibidos -eq 0 -and $respuesta.duplicados -gt 0) {
    Write-Host ''
    Write-Host 'Mensaje ya conocido: no se reencolo ni se reabrio la ventana de 24h.' -ForegroundColor Yellow
}
