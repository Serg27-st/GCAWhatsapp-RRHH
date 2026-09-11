using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RRHH.WhatsApp.Api.TiempoReal;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Contracts.TiempoReal;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Servicios;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// Sección 9.6.3: los avisos para analistas salen de la outbox y llegan al canal en vivo.
/// <para>
/// El evento <c>AnalistaNotificado</c> se publicaba desde el primer día sin que nadie lo
/// consumiera: el aviso de 48h (Regla 9), el multi-cuenta (Regla 6), el de escalamiento (Regla 2)
/// y el de transferencia (Regla 8) se calculaban bien y no llegaban a ninguna persona.
/// </para>
/// </summary>
public class DifusorNotificacionesTests : IDisposable
{
    /// <summary>
    /// Recoge lo que se habría empujado al hub, sin levantar SignalR.
    /// <para>
    /// La lista se protege con candado porque el difusor corre en su propio hilo: sin eso, leerla
    /// desde la prueba mientras el bucle escribe hace fallar el recuento de vez en cuando.
    /// </para>
    /// </summary>
    private sealed class AvisoEspia : IAvisoBandeja
    {
        private readonly List<NotificacionAnalista> _enviados = [];
        private readonly Lock _candado = new();

        public IReadOnlyList<NotificacionAnalista> Enviados
        {
            get { lock (_candado) { return [.. _enviados]; } }
        }

        public Task EnviarAsync(NotificacionAnalista aviso, CancellationToken ct = default)
        {
            lock (_candado) { _enviados.Add(aviso); }

            return Task.CompletedTask;
        }
    }

    private readonly RrhhDbContext _db;
    private readonly IEventoSistemaService _eventos;
    private readonly AvisoEspia _espia = new();
    private readonly DifusorNotificaciones _difusor;

    public DifusorNotificacionesTests()
    {
        _db = new RrhhDbContext(new DbContextOptionsBuilder<RrhhDbContext>()
            .UseInMemoryDatabase($"difusor-{Guid.NewGuid()}")
            .Options);

        _db.Database.EnsureCreated();

        _eventos = new EventoSistemaService(_db, NullLogger<EventoSistemaService>.Instance);

        var servicios = new ServiceCollection();
        servicios.AddScoped(_ => _eventos);

        _difusor = new DifusorNotificaciones(
            servicios.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            _espia,
            NullLogger<DifusorNotificaciones>.Instance);
    }

    /// <summary>
    /// Corre el bucle hasta que difunda lo esperado y lo detiene.
    /// <para>
    /// Se espera sobre el espía y no sobre la base: el difusor usa el mismo <c>DbContext</c> desde
    /// su propio hilo, y EF Core no admite dos operaciones a la vez sobre la misma instancia.
    /// Consultarla desde acá mientras el bucle trabaja hacía fallar la prueba de forma
    /// intermitente.
    /// </para>
    /// </summary>
    private async Task DifundirAsync(int esperados = 1)
    {
        using var corte = new CancellationTokenSource();

        var tarea = _difusor.StartAsync(corte.Token);

        if (esperados > 0)
        {
            for (var i = 0; i < 100 && _espia.Enviados.Count < esperados; i++)
                await Task.Delay(20, CancellationToken.None);
        }
        else
        {
            // No hay nada que esperar: se le da una vuelta al bucle para comprobar que
            // efectivamente no difunde, y se corta.
            await Task.Delay(300, CancellationToken.None);
        }

        await corte.CancelAsync();
        await _difusor.StopAsync(CancellationToken.None);
        await tarea;
    }

    [Fact]
    public async Task Un_aviso_pendiente_llega_al_analista_y_queda_procesado()
    {
        await _eventos.PublicarAsync(TiposEvento.AnalistaNotificado,
            new { AnalistaId = 10, Mensaje = "El postulante no completo el formulario.", ConversacionId = 3 },
            Guid.NewGuid());

        await DifundirAsync();

        var aviso = Assert.Single(_espia.Enviados);

        Assert.Equal(10, aviso.AnalistaId);
        Assert.Equal(3, aviso.ConversacionId);
        Assert.Contains("formulario", aviso.Mensaje);

        var evento = await _db.EventosSistema.AsNoTracking().FirstAsync();
        Assert.Equal(EstadoEvento.Procesado, evento.Estado);
    }

    [Fact]
    public async Task Solo_atiende_los_avisos_y_deja_el_resto_para_el_Worker()
    {
        // Los dos bucles leen la misma tabla con filtros disjuntos: por eso nunca se pisan la
        // misma fila, pese a que EventosSistema no tiene reserva.
        await _eventos.PublicarAsync(TiposEvento.AnalistaNotificado,
            new { AnalistaId = 10, Mensaje = "algo", ConversacionId = 1 }, Guid.NewGuid());

        await _eventos.PublicarAsync(TiposEvento.MensajeEntranteRecibido,
            new { ConversacionId = 1 }, Guid.NewGuid());

        await DifundirAsync();

        Assert.Single(_espia.Enviados);

        var delWorker = await _db.EventosSistema.AsNoTracking()
            .FirstAsync(e => e.Tipo == TiposEvento.MensajeEntranteRecibido);

        Assert.Equal(EstadoEvento.Pendiente, delWorker.Estado);
    }

    [Fact]
    public async Task Un_payload_sin_analista_destino_se_marca_fallido_y_no_se_difunde()
    {
        await _eventos.PublicarAsync(TiposEvento.AnalistaNotificado,
            new { Mensaje = "sin destino" }, Guid.NewGuid());

        await DifundirAsync(esperados: 0);

        Assert.Empty(_espia.Enviados);

        var evento = await _db.EventosSistema.AsNoTracking().FirstAsync();
        Assert.NotNull(evento.UltimoError);
    }

    [Fact]
    public async Task Varios_avisos_se_difunden_en_el_mismo_lote()
    {
        foreach (var analistaId in new[] { 10, 11, 12 })
        {
            await _eventos.PublicarAsync(TiposEvento.AnalistaNotificado,
                new { AnalistaId = analistaId, Mensaje = "novedad", ConversacionId = 1 }, Guid.NewGuid());
        }

        await DifundirAsync(esperados: 3);

        Assert.Equal(3, _espia.Enviados.Count);
        Assert.Equal([10, 11, 12], _espia.Enviados.Select(a => a.AnalistaId).Order().ToArray());
    }

    [Fact]
    public void El_difusor_y_el_Worker_no_comparten_ningun_tipo()
    {
        // Si alguna vez se solapan, los dos procesos tomarian la misma fila y el aviso saldria
        // dos veces o la regla correria dos veces. Conviene que falle aca y no en produccion.
        Assert.Empty(DifusorNotificaciones.TiposQueAtiende.Intersect(ProcesadorOutbox.TiposQueAtiende));
    }

    public void Dispose() => _db.Dispose();
}
