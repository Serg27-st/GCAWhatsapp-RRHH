namespace RRHH.WhatsApp.Domain.Reglas;

/// <summary>
/// Patron Strategy: cada una de las reglas de negocio del dossier es una unidad independiente
/// y testeable. Agregar una regla nueva no obliga a tocar las demas, y cada una tiene su propia
/// prueba unitaria (Seccion 9.6.6).
/// </summary>
public interface IReglaNegocio
{
    /// <summary>Codigo de la regla en el dossier, ej. "R02" para el escalamiento por inactividad.</summary>
    string Codigo { get; }

    string Descripcion { get; }

    /// <summary>Orden de evaluacion. Menor primero. La Regla 15 corre antes que cualquier otra que envie.</summary>
    int Prioridad { get; }

    /// <summary>Filtro barato para no evaluar reglas que no vienen al caso en este disparador.</summary>
    bool Aplica(ContextoRegla contexto);

    Task<ResultadoRegla> EvaluarAsync(ContextoRegla contexto, CancellationToken ct = default);
}

/// <summary>
/// Orquesta la evaluacion de las reglas en orden de prioridad y devuelve las acciones resultantes.
/// No envia mensajes ni escribe en las tablas de otros modulos: solo decide.
/// </summary>
public interface IMotorReglas
{
    Task<IReadOnlyList<AccionRegla>> ProcesarAsync(ContextoRegla contexto, CancellationToken ct = default);
}
