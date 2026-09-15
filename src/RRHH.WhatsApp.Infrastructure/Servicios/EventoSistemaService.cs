using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Infrastructure.Servicios;

/// <summary>
/// Outbox. El webhook publica aca y responde; el Worker consume despues. Esa separacion es la que
/// permite cumplir el limite de 5 segundos que 360dialog da para responder 200 sin acoplar la
/// ingesta a la velocidad del motor de reglas.
/// </summary>
public sealed class EventoSistemaService(RrhhDbContext db, TimeProvider reloj, ILogger<EventoSistemaService> log)
    : IEventoSistemaService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task PublicarAsync(string tipo, object payload, Guid correlationId, CancellationToken ct = default)
    {
        db.EventosSistema.Add(new EventoSistema
        {
            Tipo = tipo,
            Payload = JsonSerializer.Serialize(payload, Json),
            Estado = EstadoEvento.Pendiente,
            CorrelationId = correlationId,
            FechaCreacion = reloj.GetUtcNow().UtcDateTime
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<EventoSistema>> ObtenerPendientesAsync(
        int maximo, IReadOnlyCollection<string> tipos, CancellationToken ct = default)
    {
        var consulta = db.EventosSistema
            .AsNoTracking()
            .Where(e => e.Estado == EstadoEvento.Pendiente);

        // Los eventos salen sin seguimiento porque cada uno se procesa despues en su propio
        // ambito, con su propio DbContext: asi un evento que falla no deja el ChangeTracker
        // sucio para el siguiente del lote.
        if (tipos.Count > 0)
            consulta = consulta.Where(e => tipos.Contains(e.Tipo));

        return await consulta
            .OrderBy(e => e.FechaCreacion)
            .Take(maximo)
            .ToListAsync(ct);
    }

    public async Task MarcarProcesadoAsync(long eventoId, CancellationToken ct = default)
    {
        var evento = await db.EventosSistema.FirstAsync(e => e.EventoId == eventoId, ct);

        evento.Estado = EstadoEvento.Procesado;
        evento.FechaProcesado = reloj.GetUtcNow().UtcDateTime;

        await db.SaveChangesAsync(ct);
    }

    public async Task MarcarFallidoAsync(long eventoId, string error, int reintentosMaximos, CancellationToken ct = default)
    {
        var evento = await db.EventosSistema.FirstAsync(e => e.EventoId == eventoId, ct);

        evento.IntentosProcesamiento++;
        evento.UltimoError = error.Length <= 2000 ? error : error[..2000];

        // Vuelve a Pendiente mientras queden intentos. Agotados, queda en Fallido con su ultimo
        // error: un evento que falla repetido tiene que quedar visible, no perderse en silencio.
        if (evento.IntentosProcesamiento >= reintentosMaximos)
        {
            evento.Estado = EstadoEvento.Fallido;

            log.LogError("El evento {EventoId} ({Tipo}) agoto {Intentos} intentos: {Error}",
                eventoId, evento.Tipo, evento.IntentosProcesamiento, evento.UltimoError);
        }

        await db.SaveChangesAsync(ct);
    }
}
