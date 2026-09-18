using RRHH.WhatsApp.Contracts.Bandeja;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;

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
            otrasCuentas ?? [],
            // FUN-05: el aviso a Jefatura ya salio y el postulante todavia espera. Se calcula con los
            // mismos datos que la regla, para que la pantalla no diga otra cosa que el barrido.
            Vencida: c.FechaAvisoSegundoNivel is not null
                && ultimoEntrante is not null
                && (c.FechaUltimaRespuestaAnalista is null || c.FechaUltimaRespuestaAnalista < ultimoEntrante));
    }

    public static MensajeResumen AResumen(this Mensaje m)
    {
        var fallido = m.EstadoEntrega == EstadoEntrega.Fallido;

        return new(m.MensajeId,
            m.Direccion.ToString(),
            m.Contenido,
            m.Plantilla?.Clave,
            m.AnalistaId,
            m.FechaEnvio,
            m.EstadoEntrega.ToString(),
            [.. m.Adjuntos.OrderBy(a => a.AdjuntoId).Select(a => a.AResumen())],
            // FUN-13 (M2): sin el motivo, el analista ve que no llego pero no sabe si reintentar o
            // cambiar de camino (una plantilla, otro numero).
            Error: fallido ? m.ErrorProveedor : null,
            // Solo lo que escribio una persona y no llego seguro. Un fallo ambiguo pudo haber salido
            // y reenviarlo lo duplicaria; uno transitorio ya lo reintenta el Worker; el bot vuelve a
            // decidir solo. La plantilla se vuelve a elegir con sus parametros.
            Reintentable: fallido
                && m.Direccion == DireccionMensaje.Saliente
                && m.ClaseFallo == ClaseFallo.Permanente
                && m.AnalistaId is not null
                && m.PlantillaId is null);
    }

    /// <summary>FUN-14: sin la ruta, que es interna. La bandeja pide el archivo por su id.</summary>
    public static AdjuntoResumen AResumen(this MensajeAdjunto a) =>
        new(a.AdjuntoId, a.TipoMedio, a.NombreArchivo, a.Estado.ToString());

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
