using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Infrastructure.Proveedores;

/// <summary>
/// Arma los cuerpos JSON de la Cloud API de Meta.
/// <para>
/// Lo comparten los dos proveedores porque 360dialog es un paso a través de la Cloud API: el
/// cuerpo que se manda es el mismo y solo cambian la URL, la cabecera de autenticación y la firma
/// del webhook. Duplicarlo llevaría a que un arreglo en uno no llegue al otro.
/// </para>
/// </summary>
public static class CuerposMensaje
{
    /// <summary>Tope de filas que WhatsApp admite en una lista interactiva.</summary>
    public const int MaximoOpcionesLista = 10;

    public static object Texto(string telefonoE164, string texto) => new
    {
        messaging_product = "whatsapp",
        recipient_type = "individual",
        to = SoloDigitos(telefonoE164),
        type = "text",
        text = new { body = texto }
    };

    /// <summary>
    /// Devuelve el motivo del rechazo si la plantilla no se puede enviar, o nulo si está en
    /// condiciones. Se valida antes de salir a la red: una plantilla sin aprobar es exactamente
    /// el envío que provoca sanciones sobre la línea (Sección 2.4).
    /// </summary>
    public static string? ValidarPlantilla(Plantilla plantilla, IReadOnlyList<string> parametros)
    {
        if (!plantilla.Activa)
            return $"La plantilla '{plantilla.Clave}' no esta activa: falta su aprobacion en Meta.";

        if (parametros.Count != plantilla.CantidadParametros)
        {
            return $"La plantilla '{plantilla.Clave}' espera {plantilla.CantidadParametros} " +
                   $"parametros y recibio {parametros.Count}.";
        }

        return null;
    }

    public static object Plantilla(
        string telefonoE164, Plantilla plantilla, IReadOnlyList<string> parametros)
    {
        object[] componentes = parametros.Count == 0
            ? []
            : [
                new
                {
                    type = "body",
                    parameters = parametros.Select(p => new { type = "text", text = p }).ToArray()
                }
            ];

        return new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = SoloDigitos(telefonoE164),
            type = "template",
            template = new
            {
                name = plantilla.NombreMeta,
                language = new { code = plantilla.Idioma },
                components = componentes
            }
        };
    }

    public static object Botones(
        string telefonoE164, string texto, IReadOnlyList<BotonRespuesta> botones) => new
    {
        messaging_product = "whatsapp",
        recipient_type = "individual",
        to = SoloDigitos(telefonoE164),
        type = "interactive",
        interactive = new
        {
            type = "button",
            body = new { text = Recortar(texto, 1024) },
            action = new
            {
                buttons = botones.Select(b => new
                {
                    type = "reply",
                    reply = new { id = b.Id, title = Recortar(b.Titulo, 20) }
                }).ToArray()
            }
        }
    };

    public static object Lista(
        string telefonoE164, string texto, string textoBoton, IReadOnlyList<BotonRespuesta> opciones) => new
    {
        messaging_product = "whatsapp",
        recipient_type = "individual",
        to = SoloDigitos(telefonoE164),
        type = "interactive",
        interactive = new
        {
            type = "list",
            body = new { text = Recortar(texto, 1024) },
            action = new
            {
                button = Recortar(textoBoton, 20),
                sections = new[]
                {
                    new
                    {
                        title = "Opciones",
                        rows = opciones.Take(MaximoOpcionesLista).Select(o => new
                        {
                            id = o.Id,
                            title = Recortar(o.Titulo, 24)
                        }).ToArray()
                    }
                }
            }
        }
    };

    /// <summary>WhatsApp rechaza el mensaje entero si un texto pasa su límite; se corta antes.</summary>
    public static string Recortar(string texto, int maximo) =>
        texto.Length <= maximo ? texto : texto[..maximo];

    /// <summary>Meta espera el número sin signo ni separadores.</summary>
    public static string SoloDigitos(string telefono) => new([.. telefono.Where(char.IsDigit)]);
}
