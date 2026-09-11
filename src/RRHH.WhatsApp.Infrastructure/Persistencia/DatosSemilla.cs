using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;

namespace RRHH.WhatsApp.Infrastructure.Persistencia;

/// <summary>
/// Catalogos que el sistema necesita para arrancar: las etapas del tablero (Regla 13), los
/// parametros ajustables sin redeploy y el catalogo de plantillas.
/// </summary>
public static class DatosSemilla
{
    /// <summary>Fecha fija: EF Core exige valores constantes en HasData para que la migracion sea reproducible.</summary>
    private static readonly DateTime Origen = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static void Aplicar(ModelBuilder modelo)
    {
        // Regla 13: columnas del tablero kanban.
        modelo.Entity<EtapaKanban>().HasData(
            new EtapaKanban { EtapaId = 1, Nombre = "Postulante nuevo", Orden = 1, EsFinal = false },
            new EtapaKanban { EtapaId = 2, Nombre = "En revision", Orden = 2, EsFinal = false },
            new EtapaKanban { EtapaId = 3, Nombre = "Entrevista", Orden = 3, EsFinal = false },
            new EtapaKanban { EtapaId = 4, Nombre = "Contratado", Orden = 4, EsFinal = true },
            new EtapaKanban { EtapaId = 5, Nombre = "Descartado", Orden = 5, EsFinal = true });

        modelo.Entity<ConfiguracionRegla>().HasData(
            Config(ClavesConfiguracion.EscalamientoHoras, "2",
                "Regla 2: horas sin respuesta del titular antes de escalar al respaldo fijo."),
            Config(ClavesConfiguracion.EscalamientoSoloHorarioLaboral, "true",
                "Si es true, esas horas solo corren dentro del horario de atencion, para que un mensaje recibido al cierre no escale de madrugada."),
            Config(ClavesConfiguracion.RecordatorioJobFormsHoras, "24",
                "Regla 9: horas tras enviar el link antes de recordarle al postulante que complete el formulario."),
            Config(ClavesConfiguracion.AvisoAnalistaJobFormsHoras, "48",
                "Regla 9: horas tras enviar el link antes de avisarle al analista que el postulante no completo el formulario."),
            Config(ClavesConfiguracion.RepreguntaEmpresaDias, "3",
                "Regla 9: dias de inactividad tras los cuales el bot vuelve a preguntar la empresa."),
            Config(ClavesConfiguracion.ArchivadoDias, "90",
                "Regla 16: dias sin actividad y sin marca de contratado o reingreso antes del archivado definitivo."),
            Config(ClavesConfiguracion.RetencionCvDias, "365",
                "Regla 17: dias de retencion de CVs antes de la purga, alineado a la Ley de Proteccion de Datos Personales."),
            Config(ClavesConfiguracion.VersionAvisoPrivacidad, "2026-08-v1",
                "Regla 17: version del aviso de privacidad vigente, que se sella en cada respuesta de JobForms."),
            Config(ClavesConfiguracion.EnvioMaximoPorSegundo, "10",
                "Seccion 9.6.4: tope de velocidad de envio saliente hacia Meta, para no repetir el patron que causo el bloqueo."),
            Config(ClavesConfiguracion.ReintentosMaximosEvento, "5",
                "Seccion 9.6.2: intentos de procesamiento de un evento de la outbox antes de marcarlo como fallido."),
            Config(ClavesConfiguracion.EnvioReintentosMaximos, "4",
                "Intentos de un saliente rechazado por causa transitoria antes de darlo por perdido."),
            Config(ClavesConfiguracion.EnvioReintentoBaseSegundos, "60",
                "Base del retroceso exponencial entre reintentos de envio: 1m, 2m, 4m."));

        // Catalogo de plantillas. Quedan INACTIVAS a proposito: cada una debe registrarse y
        // aprobarse en Meta, y recien ahi se activa desde la administracion. El texto de abajo es
        // el borrador a presentar; NombreMeta debe coincidir con el nombre aprobado.
        modelo.Entity<Plantilla>().HasData(
            Borrador(1, ClavesPlantilla.FueraDeHorario, "rrhh_fuera_horario",
                "Hola. Nuestro horario de atencion es {{1}}. Tu mensaje ya quedo registrado y un analista te respondera apenas retomemos la jornada.", 1),
            Borrador(2, ClavesPlantilla.RecordatorioJobForms, "rrhh_recordatorio_formulario",
                "Hola {{1}}. Notamos que aun no completas tu ficha de postulacion para la vacante {{2}}. Puedes hacerlo aqui: {{3}}", 3),
            Borrador(3, ClavesPlantilla.ConfirmacionJobForms, "rrhh_confirmacion_formulario",
                "Gracias {{1}}. Recibimos tu ficha para la vacante {{2}}. Un analista revisara tu postulacion y te escribira por este mismo chat.", 2),
            Borrador(4, ClavesPlantilla.CierreCortesia, "rrhh_cierre_cortesia",
                "Hola {{1}}. Agradecemos tu interes en la vacante {{2}}. En esta oportunidad continuaremos con otros perfiles, pero mantendremos tus datos para futuras convocatorias.", 2),
            Borrador(5, ClavesPlantilla.VacanteCerrada, "rrhh_vacante_cerrada",
                "La vacante {{1}} ya fue cubierta. Puedes revisar nuestras otras convocatorias activas respondiendo a este mensaje.", 1),
            Borrador(6, ClavesPlantilla.ReaperturaConversacion, "rrhh_reapertura",
                "Hola {{1}}. Vemos que vuelves a escribirnos. Para ayudarte mejor, indicanos a que empresa corresponde tu consulta.", 1));
    }

    private static ConfiguracionRegla Config(string clave, string valor, string descripcion) =>
        new() { Clave = clave, Valor = valor, Descripcion = descripcion, FechaActualizacion = Origen };

    private static Plantilla Borrador(int id, string clave, string nombreMeta, string texto, int parametros) =>
        new()
        {
            PlantillaId = id,
            Clave = clave,
            NombreMeta = nombreMeta,
            Categoria = CategoriaPlantilla.Utilidad,
            Idioma = "es",
            TextoAprobado = texto,
            CantidadParametros = parametros,
            Activa = false
        };
}
