using System.Security.Cryptography;
using System.Text;
using RRHH.WhatsApp.Api.Configuracion;

namespace RRHH.WhatsApp.Api.Seguridad;

/// <summary>
/// Comparacion del secreto compartido con el Apps Script del formulario (Seccion 9.6.1). Vive
/// aparte del controlador porque el limitador de velocidad necesita la misma comprobacion —para
/// separar el cupo del script real del de un desconocido (COR-15)— y una copia paralela es lo que
/// termina desincronizado el dia que alguien cambia una sola de las dos.
/// </summary>
public static class SecretoJobForms
{
    public const string Cabecera = "X-JobForms-Secreto";

    /// <summary>
    /// Comparacion de tiempo fijo: una comparacion normal filtra el secreto por el reloj. Sin
    /// secreto configurado no hay nada contra que comparar, asi que se rechaza de una.
    /// </summary>
    public static bool EsValido(IHeaderDictionary cabeceras, OpcionesJobForms opciones)
    {
        if (!opciones.EstaConfigurado)
            return false;

        if (!cabeceras.TryGetValue(Cabecera, out var recibido))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(recibido.ToString()),
            Encoding.UTF8.GetBytes(opciones.SecretoWebhook));
    }
}
