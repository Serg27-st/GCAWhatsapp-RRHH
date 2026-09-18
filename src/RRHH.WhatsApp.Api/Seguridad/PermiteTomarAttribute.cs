namespace RRHH.WhatsApp.Api.Seguridad;

/// <summary>
/// FUN-01: la accion se puede ejecutar con nivel <see cref="Domain.Enums.NivelAcceso.Lectura"/>.
/// <para>
/// Es la excepcion que necesita «tomar» una conversacion de la bandeja general: quien la toma
/// todavia no la tiene a cargo —por eso su nivel es Lectura—, y es justamente la accion con la que
/// pasa a tenerla. El servicio igual exige que trabaje la cuenta (Regla 4).
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PermiteTomarAttribute : Attribute;
