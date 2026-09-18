using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Servicios;

namespace RRHH.WhatsApp.Tests;

/// <summary>
/// Arma servicios reales con sus dependencias, para las pruebas que no levantan el contenedor.
/// <para>
/// Existe porque un servicio con seis colaboradores no debería obligar a que treinta pruebas
/// escriban el mismo grafo: cuando el servicio suma uno, se cambia acá y no en cada archivo.
/// </para>
/// </summary>
internal static class ServiciosDePrueba
{
    /// <summary>El grafo real de <see cref="ConversacionService"/>, con el reloj que pase la prueba.</summary>
    public static ConversacionService Conversaciones(RrhhDbContext db, TimeProvider? reloj = null)
    {
        var tiempo = reloj ?? TimeProvider.System;

        return new ConversacionService(
            db,
            new CuentaService(db, new AlertaOperativaService(db, tiempo), tiempo),
            new AusenciaService(db, tiempo),
            new HorarioAtencionService(db, tiempo, NullLogger<HorarioAtencionService>.Instance),
            new ConfiguracionReglasService(db, new MemoryCache(new MemoryCacheOptions()), tiempo),
            tiempo,
            NullLogger<ConversacionService>.Instance);
    }

    /// <summary>
    /// El grafo real de <see cref="AnalistaService"/>. Tiene colaboradores porque dar de baja a alguien
    /// mueve su cartera y deja alertas en sus cuentas (FUN-19).
    /// </summary>
    public static AnalistaService Analistas(RrhhDbContext db, TimeProvider? reloj = null)
    {
        var tiempo = reloj ?? TimeProvider.System;

        return new AnalistaService(
            db,
            Conversaciones(db, tiempo),
            new AlertaOperativaService(db, tiempo),
            new UnidadTrabajoEf(db),
            tiempo);
    }

    /// <summary>
    /// El grafo real de <see cref="PostulanteService"/>. Tiene tantos colaboradores porque la
    /// anonimizacion de la Regla 17 alcanza al hilo, sus mensajes, sus archivos, la outbox y la
    /// auditoria (FUN-16).
    /// </summary>
    public static PostulanteService Postulantes(
        RrhhDbContext db, IAlmacenamientoCv cv, IAlmacenamientoAdjuntos adjuntos, TimeProvider? reloj = null)
    {
        var tiempo = reloj ?? TimeProvider.System;

        return new PostulanteService(
            db,
            cv,
            adjuntos,
            Conversaciones(db, tiempo),
            new MensajeService(db, tiempo, NullLogger<MensajeService>.Instance),
            new EventoSistemaService(db, tiempo, NullLogger<EventoSistemaService>.Instance),
            new AuditoriaService(db, tiempo),
            new UnidadTrabajoEf(db),
            tiempo,
            NullLogger<PostulanteService>.Instance);
    }
}
