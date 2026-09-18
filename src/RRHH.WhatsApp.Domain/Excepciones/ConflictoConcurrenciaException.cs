namespace RRHH.WhatsApp.Domain.Excepciones;

/// <summary>
/// Dos personas actuaron sobre la misma fila y gano la primera (FUN-01). No es un error del sistema
/// ni del que perdio: la Api lo traduce a un 409 para que la pantalla se refresque y lo muestre.
/// </summary>
public sealed class ConflictoConcurrenciaException(string mensaje) : InvalidOperationException(mensaje);
