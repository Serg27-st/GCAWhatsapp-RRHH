using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Reporting;
using RRHH.WhatsApp.Reporting.Persistencia;

namespace RRHH.WhatsApp.Tests.Reporting;

/// <summary>
/// Base en memoria para el panel de gerencia, con helpers para escribir el historial que las
/// metricas van a leer. El modelo de reporting es de solo lectura, asi que el escenario se siembra
/// por su propio contexto.
/// </summary>
internal sealed class EntornoDeMetricas : IDisposable
{
    public const int CuentaId = 7;
    public const int TitularId = 10;
    public const int RespaldoId = 11;

    /// <summary>Ancla fija: las metricas se calculan sobre un periodo y conviene que no se mueva.</summary>
    public static readonly DateTime Ahora = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

    public ReportingDbContext Db { get; }
    private readonly SembradorDbContext _siembra;
    public IReportingReadModel Metricas { get; }

    private long _mensajeId;
    private int _postulacionId;

    public EntornoDeMetricas()
    {
        var nombre = $"metricas-{Guid.NewGuid()}";

        _siembra = new SembradorDbContext(new DbContextOptionsBuilder<SembradorDbContext>()
            .UseInMemoryDatabase(nombre)
            .Options);

        var opciones = new DbContextOptionsBuilder<ReportingDbContext>()
            .UseInMemoryDatabase(nombre)
            .Options;

        Db = new ReportingDbContext(opciones);
        Db.Database.EnsureCreated();
        _siembra.Database.EnsureCreated();

        Sembrar();

        Metricas = new ReportingReadModel(Db);
    }

    private void Sembrar()
    {
        _siembra.Cuentas.Add(new Cuenta { CuentaId = CuentaId, Nombre = "Alicorp", Activo = true });

        _siembra.Analistas.AddRange(
            new Analista { AnalistaId = TitularId, Nombre = "Ana Torres", Email = "ana@gca.pe", Activo = true },
            new Analista { AnalistaId = RespaldoId, Nombre = "Luis Vega", Email = "luis@gca.pe", Activo = true });

        _siembra.ConfiguracionReglas.Add(new ConfiguracionRegla
        {
            Clave = ClavesConfiguracion.EscalamientoHoras,
            Valor = "2",
            FechaActualizacion = Ahora
        });

        Guardar();
    }

    /// <summary>
    /// Un hilo que recibe un mensaje y, si <paramref name="minutosHastaRespuesta"/> no es nulo,
    /// obtiene respuesta de un analista a esa altura.
    /// </summary>
    public void ConversacionConRespuesta(
        int conversacionId, DateTime entrante, double? minutosHastaRespuesta, int? analistaId = TitularId)
    {
        _siembra.Mensajes.Add(Mensaje(conversacionId, DireccionMensaje.Entrante, null, entrante));

        if (minutosHastaRespuesta is { } minutos)
        {
            _siembra.Mensajes.Add(Mensaje(
                conversacionId, DireccionMensaje.Saliente, analistaId, entrante.AddMinutes(minutos)));
        }

        Guardar();
    }

    /// <summary>Un mensaje del bot: sale sin analista y no cuenta como primera respuesta.</summary>
    public void MensajeDelBot(int conversacionId, DateTime fecha)
    {
        _siembra.Mensajes.Add(Mensaje(conversacionId, DireccionMensaje.Saliente, null, fecha));

        Guardar();
    }

    public int Postulacion(EstadoPostulacion estado, DateTime creacion, int? analistaId = TitularId)
    {
        var id = ++_postulacionId;

        _siembra.Postulaciones.Add(new Postulacion
        {
            PostulacionId = id,
            PostulanteId = id,
            HcId = 1,
            CuentaId = CuentaId,
            AnalistaAsignadoId = analistaId,
            EtapaKanbanId = 1,
            Estado = estado,
            FechaCreacion = creacion,
            FechaUltimaActividad = creacion
        });

        Guardar();

        return id;
    }

    public void Escalamiento(DateTime fecha)
    {
        _siembra.Auditorias.Add(new Auditoria
        {
            EntidadTipo = nameof(Conversacion),
            EntidadId = "1",
            Accion = "Escalamiento",
            Fecha = fecha
        });

        Guardar();
    }

    private Mensaje Mensaje(int conversacionId, DireccionMensaje direccion, int? analistaId, DateTime fecha) =>
        new()
        {
            MensajeId = ++_mensajeId,
            ConversacionId = conversacionId,
            Direccion = direccion,
            Contenido = ".",
            AnalistaId = analistaId,
            FechaEnvio = fecha,
            EstadoEntrega = EstadoEntrega.Entregado
        };

    /// <summary>La siembra va por el contexto de escritura; el de reporting solo lee.</summary>
    private void Guardar() => _siembra.SaveChanges();

    public void Dispose()
    {
        Db.Dispose();
        _siembra.Dispose();
    }
}
