namespace RRHH.WhatsApp.Tests.Proveedores;

/// <summary>
/// Payloads con la forma de la Cloud API de Meta, que es la que 360dialog reenvia. Estan escritos
/// a mano y no generados, para que si Meta cambia el formato la prueba falle y avise.
/// </summary>
internal static class PayloadsDePrueba
{
    public const string MensajeDeTexto = """
    {
      "object": "whatsapp_business_account",
      "entry": [{
        "id": "102290129340398",
        "changes": [{
          "field": "messages",
          "value": {
            "messaging_product": "whatsapp",
            "metadata": { "display_phone_number": "51999888777", "phone_number_id": "1234" },
            "contacts": [{ "profile": { "name": "Maria Quispe" }, "wa_id": "51987654321" }],
            "messages": [{
              "from": "51987654321",
              "id": "wamid.HBgLNTE5ODc2NTQzMjEVAgASGBQ",
              "timestamp": "1755600000",
              "type": "text",
              "text": { "body": "Hola, vi el aviso de trabajo" }
            }]
          }
        }]
      }]
    }
    """;

    public const string RespuestaDeBoton = """
    {
      "object": "whatsapp_business_account",
      "entry": [{
        "id": "102290129340398",
        "changes": [{
          "field": "messages",
          "value": {
            "contacts": [{ "profile": { "name": "Maria Quispe" }, "wa_id": "51987654321" }],
            "messages": [{
              "from": "51987654321",
              "id": "wamid.BOTON1",
              "timestamp": "1755600100",
              "type": "interactive",
              "interactive": {
                "type": "button_reply",
                "button_reply": { "id": "cuenta_7", "title": "Alicorp" }
              }
            }]
          }
        }]
      }]
    }
    """;

    public const string RespuestaDeLista = """
    {
      "object": "whatsapp_business_account",
      "entry": [{
        "changes": [{
          "field": "messages",
          "value": {
            "messages": [{
              "from": "51987654321",
              "id": "wamid.LISTA1",
              "timestamp": "1755600200",
              "type": "interactive",
              "interactive": {
                "type": "list_reply",
                "list_reply": { "id": "hc_42", "title": "Operario de produccion" }
              }
            }]
          }
        }]
      }]
    }
    """;

    public const string BotonDePlantilla = """
    {
      "object": "whatsapp_business_account",
      "entry": [{
        "changes": [{
          "field": "messages",
          "value": {
            "messages": [{
              "from": "51987654321",
              "id": "wamid.QUICK1",
              "timestamp": "1755600300",
              "type": "button",
              "button": { "payload": "confirmar_entrevista", "text": "Confirmo" }
            }]
          }
        }]
      }]
    }
    """;

    public const string Adjunto = """
    {
      "object": "whatsapp_business_account",
      "entry": [{
        "changes": [{
          "field": "messages",
          "value": {
            "messages": [{
              "from": "51987654321",
              "id": "wamid.DOC1",
              "timestamp": "1755600400",
              "type": "document",
              "document": {
                "filename": "cv.pdf",
                "mime_type": "application/pdf",
                "sha256": "0c7ce1b5a2c4b8d7e3f1a9b2c6d4e8f0a1b3c5d7e9f2a4b6c8d0e2f4a6b8c0d2",
                "id": "1037543291543636"
              }
            }]
          }
        }]
      }]
    }
    """;

    /// <summary>Una foto del DNI con texto: la leyenda es lo que la persona escribió.</summary>
    public const string ImagenConLeyenda = """
    {
      "object": "whatsapp_business_account",
      "entry": [{
        "changes": [{
          "field": "messages",
          "value": {
            "messages": [{
              "from": "51987654321",
              "id": "wamid.IMG1",
              "timestamp": "1755600410",
              "type": "image",
              "image": {
                "caption": "Mi DNI por ambos lados",
                "mime_type": "image/jpeg",
                "sha256": "b1d2c3e4f5a6b7c8d9e0f1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b1c2",
                "id": "2154839923311120"
              }
            }]
          }
        }]
      }]
    }
    """;

    /// <summary>Una nota de voz: sin nombre ni leyenda, con el códec en el mime.</summary>
    public const string NotaDeVoz = """
    {
      "object": "whatsapp_business_account",
      "entry": [{
        "changes": [{
          "field": "messages",
          "value": {
            "messages": [{
              "from": "51987654321",
              "id": "wamid.AUD1",
              "timestamp": "1755600420",
              "type": "audio",
              "audio": {
                "mime_type": "audio/ogg; codecs=opus",
                "sha256": "c2d3e4f5a6b7c8d9e0f1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b1c2d3",
                "id": "8854120937765432",
                "voice": true
              }
            }]
          }
        }]
      }]
    }
    """;

    public const string Sticker = """
    {
      "object": "whatsapp_business_account",
      "entry": [{
        "changes": [{
          "field": "messages",
          "value": {
            "messages": [{
              "from": "51987654321",
              "id": "wamid.STK1",
              "timestamp": "1755600430",
              "type": "sticker",
              "sticker": { "mime_type": "image/webp", "id": "5512093847712345", "animated": false }
            }]
          }
        }]
      }]
    }
    """;

    /// <summary>Una ubicación: no es un archivo, no hay nada que descargar.</summary>
    public const string Ubicacion = """
    {
      "object": "whatsapp_business_account",
      "entry": [{
        "changes": [{
          "field": "messages",
          "value": {
            "messages": [{
              "from": "51987654321",
              "id": "wamid.LOC1",
              "timestamp": "1755600440",
              "type": "location",
              "location": { "latitude": -12.0464, "longitude": -77.0428 }
            }]
          }
        }]
      }]
    }
    """;

    public const string AcusesDeEntrega = """
    {
      "object": "whatsapp_business_account",
      "entry": [{
        "changes": [{
          "field": "messages",
          "value": {
            "statuses": [
              {
                "id": "wamid.SALIENTE1",
                "status": "delivered",
                "timestamp": "1755600500",
                "recipient_id": "51987654321"
              },
              {
                "id": "wamid.SALIENTE2",
                "status": "failed",
                "timestamp": "1755600600",
                "recipient_id": "51987654321",
                "errors": [{ "code": 131047, "title": "Re-engagement message", "message": "Ventana cerrada" }]
              }
            ]
          }
        }]
      }]
    }
    """;

    /// <summary>Dos mensajes en un solo evento: Meta agrupa cuando llegan juntos.</summary>
    public const string DosMensajes = """
    {
      "object": "whatsapp_business_account",
      "entry": [{
        "changes": [{
          "field": "messages",
          "value": {
            "contacts": [{ "profile": { "name": "Jose Ramos" }, "wa_id": "51911222333" }],
            "messages": [
              {
                "from": "51911222333",
                "id": "wamid.A",
                "timestamp": "1755600700",
                "type": "text",
                "text": { "body": "Buenas tardes" }
              },
              {
                "from": "51911222333",
                "id": "wamid.B",
                "timestamp": "1755600701",
                "type": "text",
                "text": { "body": "Quiero postular" }
              }
            ]
          }
        }]
      }]
    }
    """;

    public const string SinMensajes = """
    { "object": "whatsapp_business_account", "entry": [] }
    """;
}
