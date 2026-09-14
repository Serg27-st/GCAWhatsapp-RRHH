namespace RRHH.WhatsApp.Worker;

/// <summary>
/// Nombre con el que el Worker se registra como servicio de Windows (V26). Coincide con el que usa
/// <c>scripts/instalar-servicio-worker.ps1</c>: es como el administrador de servicios lo encuentra
/// para reiniciarlo cuando el proceso termina con error.
/// </summary>
public static class ServicioWindows
{
    public const string Nombre = "RRHH WhatsApp Worker";
}
