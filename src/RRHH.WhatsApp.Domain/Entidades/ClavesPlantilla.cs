namespace RRHH.WhatsApp.Domain.Entidades;

/// <summary>
/// Claves internas con las que el codigo pide una plantilla a <c>IPlantillaService</c>.
/// Cada una debe existir en la tabla Plantillas y estar aprobada por Meta antes de salir a
/// produccion; mientras <c>Activa</c> sea false, la regla que la necesita no puede enviar.
/// </summary>
public static class ClavesPlantilla
{
    /// <summary>Regla 3: informa el horario de atencion cuando el mensaje llega fuera de jornada.</summary>
    public const string FueraDeHorario = "fuera_horario";

    /// <summary>Regla 9: recordatorio a las 24h a quien recibio el link y no completo el formulario.</summary>
    public const string RecordatorioJobForms = "recordatorio_24h";

    /// <summary>Regla 9: confirmacion al postulante de que su formulario se recibio.</summary>
    public const string ConfirmacionJobForms = "confirmacion_jobforms";

    /// <summary>Regla 12: cierre de cortesia cuando el postulante queda descartado.</summary>
    public const string CierreCortesia = "cierre_cortesia";

    /// <summary>Regla 20: la vacante ya fue cubierta y el enlace quedo desactivado.</summary>
    public const string VacanteCerrada = "vacante_cerrada";

    /// <summary>Regla 9: reapertura tras 3 dias de inactividad, cuando el bot vuelve a preguntar la empresa.</summary>
    public const string ReaperturaConversacion = "reapertura_conversacion";
}
