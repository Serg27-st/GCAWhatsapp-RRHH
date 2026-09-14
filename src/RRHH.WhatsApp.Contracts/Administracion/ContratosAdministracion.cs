using RRHH.WhatsApp.Contracts.Bandeja;

namespace RRHH.WhatsApp.Contracts.Administracion;

/// <summary>Vacante abierta de una cuenta, para administrarla y para abrir su tablero.</summary>
public sealed record VacanteResumen(int HcId, int CuentaId, string Titulo, string Estado, bool TieneFormulario);

/// <summary>
/// Regla 14: ausencia en curso o programada. Las fechas van en UTC; la bandeja las muestra en hora
/// de Lima.
/// </summary>
public sealed record AusenciaResumen(
    int AusenciaId,
    int AnalistaId,
    DateTime FechaInicioUtc,
    DateTime FechaFinUtc,
    string? Motivo);

/// <summary>Regla 3: el horario de una cuenta, o el general si <c>CuentaId</c> es nulo, con su descripcion legible.</summary>
public sealed record HorarioVigente(int? CuentaId, string Descripcion, IReadOnlyList<TramoHorario> Tramos);

/// <summary>Un parametro de las reglas con lo que gobierna: la clave sola no lo dice.</summary>
public sealed record ParametroRegla(string Clave, string Valor, string? Descripcion);

/// <summary>
/// Valores que en el dominio son enums. El Frontend no ve el dominio (V7), asi que se repiten aca;
/// una prueba falla si se separan.
/// </summary>
public static class Catalogos
{
    public static readonly IReadOnlyList<string> Roles = ["Analista", "Jefatura", "Sistemas"];

    public static readonly IReadOnlyList<string> TiposCampoOpcional = ["Texto", "Numero", "Fecha", "Booleano", "Seleccion"];
}
