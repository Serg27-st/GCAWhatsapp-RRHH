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
              "document": { "filename": "cv.pdf", "mime_type": "application/pdf" }
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
