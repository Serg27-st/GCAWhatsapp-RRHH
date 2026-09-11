using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;

namespace RRHH.WhatsApp.Reporting.Persistencia;

/// <summary>
/// Contexto de solo lectura del panel de gerencia. Es propio y no el de Infrastructure porque
/// Reporting no referencia a Infrastructure: la separacion es justamente lo que impide que un
/// reporte termine escribiendo en las tablas de otro modulo.
/// <para>
/// Mapea solo lo que las metricas necesitan leer, y nunca guarda: <see cref="SaveChanges"/> esta
/// cerrado a proposito.
/// </para>
/// </summary>
public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> opciones) : DbContext(opciones)
{
    public DbSet<Conversacion> Conversaciones => Set<Conversacion>();
    public DbSet<Mensaje> Mensajes => Set<Mensaje>();
    public DbSet<Postulacion> Postulaciones => Set<Postulacion>();
    public DbSet<Analista> Analistas => Set<Analista>();
    public DbSet<Cuenta> Cuentas => Set<Cuenta>();
    public DbSet<Auditoria> Auditorias => Set<Auditoria>();
    public DbSet<ConfiguracionRegla> ConfiguracionReglas => Set<ConfiguracionRegla>();

    protected override void OnConfiguring(DbContextOptionsBuilder opciones)
    {
        // Nada de seguimiento de cambios: el panel lee y no toca. Ademas evita cargar en memoria
        // el estado de miles de filas que solo se van a agregar.
        opciones.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        modelo.Entity<Conversacion>(b =>
        {
            b.ToTable("Conversaciones");
            b.HasKey(x => x.ConversacionId);
            b.Property(x => x.Estado).HasConversion<int>();
            b.Property(x => x.OrigenOptIn).HasConversion<int>();
            b.Property(x => x.RowVersion).IsRowVersion();
            b.Ignore(x => x.Postulante);
            b.Ignore(x => x.CuentaContexto);
            b.Ignore(x => x.AnalistaAtendiendo);
            b.Ignore(x => x.Mensajes);
        });

        modelo.Entity<Mensaje>(b =>
        {
            b.ToTable("Mensajes");
            b.HasKey(x => x.MensajeId);
            b.Property(x => x.Direccion).HasConversion<int>();
            b.Property(x => x.EstadoEntrega).HasConversion<int>();
            b.Ignore(x => x.Conversacion);
            b.Ignore(x => x.Plantilla);
        });

        modelo.Entity<Postulacion>(b =>
        {
            b.ToTable("Postulaciones");
            b.HasKey(x => x.PostulacionId);
            b.Property(x => x.Estado).HasConversion<int>();
            b.Property(x => x.RowVersion).IsRowVersion();
            b.Ignore(x => x.Postulante);
            b.Ignore(x => x.Hc);
            b.Ignore(x => x.Cuenta);
            b.Ignore(x => x.AnalistaAsignado);
            b.Ignore(x => x.EtapaKanban);
        });

        modelo.Entity<Analista>(b =>
        {
            b.ToTable("Analistas");
            b.HasKey(x => x.AnalistaId);
            b.Property(x => x.Rol).HasConversion<int>();
            b.Ignore(x => x.Cuentas);
            b.Ignore(x => x.Ausencias);
        });

        modelo.Entity<Cuenta>(b =>
        {
            b.ToTable("Cuentas");
            b.HasKey(x => x.CuentaId);
            b.Ignore(x => x.Vacantes);
            b.Ignore(x => x.Asignaciones);
        });

        modelo.Entity<Auditoria>(b =>
        {
            b.ToTable("Auditoria");
            b.HasKey(x => x.AuditoriaId);
        });

        modelo.Entity<ConfiguracionRegla>(b =>
        {
            b.ToTable("ConfiguracionReglas");
            b.HasKey(x => x.Clave);
        });
    }

    /// <summary>El panel no escribe. Que sea imposible por construccion evita tener que confiar en la disciplina.</summary>
    public override int SaveChanges() =>
        throw new InvalidOperationException("El modelo de reporting es de solo lectura.");

    public override Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        throw new InvalidOperationException("El modelo de reporting es de solo lectura.");
}
