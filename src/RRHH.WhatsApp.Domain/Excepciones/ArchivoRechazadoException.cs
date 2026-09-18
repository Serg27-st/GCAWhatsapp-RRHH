namespace RRHH.WhatsApp.Domain.Excepciones;

/// <summary>
/// El archivo no se guarda y volver a intentarlo da lo mismo: el antivirus encontro algo, pasa el
/// tope o su tipo no esta permitido (Seccion 9.6.1, V33).
/// <para>
/// Se distingue de cualquier otro fallo al guardar —un antivirus que no respondio, el recurso
/// compartido caido— porque esos si conviene reintentarlos. Hereda de
/// <see cref="InvalidOperationException"/> para que quien ya atrapaba los rechazos del CV no cambie.
/// </para>
/// </summary>
public sealed class ArchivoRechazadoException(string mensaje) : InvalidOperationException(mensaje);
