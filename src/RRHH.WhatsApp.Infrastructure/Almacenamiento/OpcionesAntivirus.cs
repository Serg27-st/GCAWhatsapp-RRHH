namespace RRHH.WhatsApp.Infrastructure.Almacenamiento;

/// <summary>
/// Escaneo antivirus de los CVs (Sección 9.6.1). Se apoya en Windows Defender, que ya viene con
/// el servidor: no agrega una licencia ni un servicio más que mantener.
/// </summary>
public sealed class OpcionesAntivirus
{
    public const string Seccion = "Cv:Antivirus";

    /// <summary>
    /// Apagarlo es una decisión explícita, no un descuido. En falso los CVs se guardan sin
    /// escanear y queda registrado en el log de arranque.
    /// </summary>
    public bool Habilitado { get; set; } = true;

    /// <summary>
    /// Ruta de MpCmdRun.exe. Vacía significa buscarlo: primero la carpeta de plataforma, que es la
    /// que Defender actualiza, y después la de Program Files.
    /// </summary>
    public string RutaMpCmdRun { get; set; } = string.Empty;

    /// <summary>Un escaneo que no termina en este tiempo se da por no disponible.</summary>
    public int TimeoutSegundos { get; set; } = 60;

    /// <summary>
    /// Qué hacer cuando el antivirus no se puede consultar.
    /// <para>
    /// En verdadero se rechaza el archivo. Es lo correcto para un endpoint público: guardar sin
    /// escanear porque el escáner estaba caído es exactamente el caso que el requisito quiere
    /// evitar, y un CV rechazado se puede volver a subir.
    /// </para>
    /// </summary>
    public bool ExigirEscaneo { get; set; } = true;
}
