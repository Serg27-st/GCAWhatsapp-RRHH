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

    public async Task<int> PurgarProcesadosAsync(int dias, int tamanoLote, CancellationToken ct = default)
    {
        var limite = reloj.GetUtcNow().UtcDateTime.AddDays(-dias);
        var borrados = 0;

        while (!ct.IsCancellationRequested)
        {
            // Por fecha de procesado y no de creacion: lo que vence es el rastro de algo ya resuelto.
            var lote = await db.EventosSistema
                .Where(e => e.Estado == EstadoEvento.Procesado && e.FechaProcesado <= limite)
                .OrderBy(e => e.EventoId)
                .Take(tamanoLote)
                .ExecuteDeleteAsync(ct);

            borrados += lote;

            // Lote incompleto: no queda nada mas viejo que el limite.
            if (lote < tamanoLote)
                break;
        }

        if (borrados > 0)
            log.LogInformation("Purga de la outbox: {Borrados} evento(s) procesados eliminados.", borrados);

        return borrados;
    }

    public async Task<int> AnonimizarPorConversacionAsync(
        IReadOnlyCollection<int> conversacionIds, CancellationToken ct = default)
    {
        var vaciados = 0;

        foreach (var id in conversacionIds)
        {
            // El payload es JSON compacto, con la propiedad en camelCase o en PascalCase segun quien lo
            // haya escrito. Se busca con el cierre —coma o llave— porque sin el, la conversacion 5
            // tambien casaria con la 51 y se borraria el rastro de otra persona.
            var camelCierre = $"\"conversacionId\":{id}}}";
            var camelComa = $"\"conversacionId\":{id},";
            var pascalCierre = $"\"ConversacionId\":{id}}}";
            var pascalComa = $"\"ConversacionId\":{id},";

            // Lo pendiente no se toca: es de hace segundos, su payload ya son solo identificadores
            // (ARQ-13) y vaciarlo dejaria al consumidor con un evento ilegible que no puede procesar ni
            // descartar. Lo que se limpia es el rastro de lo que ya no se va a procesar.
            var eventos = await db.EventosSistema
                .Where(e => e.Estado != EstadoEvento.Pendiente)
                .Where(e => e.Payload.Contains(camelCierre) || e.Payload.Contains(camelComa)
                         || e.Payload.Contains(pascalCierre) || e.Payload.Contains(pascalComa))
                .ToListAsync(ct);

            foreach (var evento in eventos)
                evento.Payload = "{}";

            vaciados += eventos.Count;
        }

        if (vaciados > 0)
            await db.SaveChangesAsync(ct);

        return vaciados;
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
