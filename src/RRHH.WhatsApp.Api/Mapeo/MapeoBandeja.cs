using RRHH.WhatsApp.Contracts.Bandeja;
using RRHH.WhatsApp.Domain.Entidades;

namespace RRHH.WhatsApp.Api.Mapeo;

/// <summary>
/// Traduce entidades de dominio a los DTOs de <c>Contracts</c>. Existe para que el Frontend nunca
/// vea una entidad: es lo que le permite hablar solo por HTTP, sin referencia al dominio
/// (desviacion V7 de docs/decisiones.md).
/// </summary>
public static class MapeoBandeja
{
    /// <summary>Duracion de la ventana de servicio de WhatsApp, medida desde el ultimo entrante.</summary>
    private static readonly TimeSpan Ventana = TimeSpan.FromHours(24);

    /// <param name="ahoraUtc">
    /// El instante lo pone quien llama (ARQ-01): con el reloj del sistema aca adentro, el borde de
    /// la ventana de 24h de la Regla 15 no se podia probar.
    /// </param>
    public static ConversacionResumen AResumen(
        this Conversacion c, DateTime ahoraUtc, IReadOnlyList<string>? otrasCuentas = null)
    {
        var ultimoEntrante = c.FechaUltimoMensajeEntrante;

        return new ConversacionResumen(
            c.ConversacionId,
            c.TelefonoE164,
            c.Postulante?.NombreCompleto,
            c.Postulante?.Dni,
            c.CuentaContextoId,
            c.CuentaContexto?.Nombre,
            c.AnalistaAtendiendoId,
            c.Estado.ToString(),
            ultimoEntrante,
            c.FechaUltimaActividad,
            // Hay algo que atender cuando el postulante escribio despues de la ultima respuesta.
            EsperandoRespuesta: ultimoEntrante is not null
                && (c.FechaUltimaRespuestaAnalista is null || c.FechaUltimaRespuestaAnalista < ultimoEntrante),
            VentanaAbierta: ultimoEntrante is { } u && ahoraUtc - u < Ventana,
            otrasCuentas ?? []);
    }

    public static MensajeResumen AResumen(this Mensaje m) =>
        new(m.MensajeId,
            m.Direccion.ToString(),
            m.Contenido,
            m.Plantilla?.Clave,
            m.AnalistaId,
            m.FechaEnvio,
            m.EstadoEntrega.ToString());

    public static PostulacionResumen AResumen(this Postulacion p) =>
        new(p.PostulacionId,
            p.PostulanteId,
            p.Postulante?.NombreCompleto,
            p.Postulante?.Dni,
            p.HcId,
            p.Hc?.Titulo,
            p.CuentaId,
            p.EtapaKanbanId,
            p.EtapaKanban?.Nombre,
            p.Estado.ToString(),
            p.AnalistaAsignadoId,
            p.FechaUltimaActividad);

    public static PlantillaResumen AResumen(this Plantilla p) =>
        new(p.PlantillaId,
            p.Clave,
            p.NombreMeta,
            p.Categoria.ToString(),
            p.TextoAprobado,
            p.CantidadParametros,
            p.Activa);
}
