using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;

namespace RRHH.WhatsApp.Tests.Reporting;

/// <summary>
/// Contexto de escritura solo para las pruebas. Existe porque <c>ReportingDbContext</c> bloquea
/// <c>SaveChanges</c> a proposito, y esa garantia vale mas que la comodidad de sembrar por el
/// mismo objeto: el proveedor en memoria comparte el almacen por nombre de base, asi que lo que
/// se escribe aca es lo que el panel lee.
/// </summary>
internal sealed class SembradorDbContext(DbContextOptions<SembradorDbContext> opciones) : DbContext(opciones)
{
    public DbSet<Conversacion> Conversaciones => Set<Conversacion>();
    public DbSet<Mensaje> Mensajes => Set<Mensaje>();
    public DbSet<Postulacion> Postulaciones => Set<Postulacion>();
    public DbSet<Analista> Analistas => Set<Analista>();
    public DbSet<Cuenta> Cuentas => Set<Cuenta>();
    public DbSet<Auditoria> Auditorias => Set<Auditoria>();
    public DbSet<ConfiguracionRegla> ConfiguracionReglas => Set<ConfiguracionRegla>();

    /// <summary>V31: la primera respuesta se mide en horas hábiles, con estos tramos (FUN-17).</summary>
    public DbSet<HorarioAtencion> HorariosAtencion => Set<HorarioAtencion>();

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        modelo.Entity<Conversacion>(b =>
        {
            b.HasKey(x => x.ConversacionId);
            b.Ignore(x => x.Postulante);
            b.Ignore(x => x.CuentaContexto);
            b.Ignore(x => x.AnalistaAtendiendo);
            b.Ignore(x => x.Mensajes);
        });

        modelo.Entity<Mensaje>(b =>
        {
            b.HasKey(x => x.MensajeId);
            b.Ignore(x => x.Conversacion);
            b.Ignore(x => x.Plantilla);
            b.Ignore(x => x.Adjuntos);
        });

        modelo.Entity<Postulacion>(b =>
        {
            b.HasKey(x => x.PostulacionId);
            b.Ignore(x => x.Postulante);
            b.Ignore(x => x.Hc);
            b.Ignore(x => x.Cuenta);
            b.Ignore(x => x.AnalistaAsignado);
            b.Ignore(x => x.EtapaKanban);
        });

        modelo.Entity<Analista>(b =>
        {
            b.HasKey(x => x.AnalistaId);
            b.Ignore(x => x.Cuentas);
            b.Ignore(x => x.Ausencias);
        });

        modelo.Entity<Cuenta>(b =>
        {
            b.HasKey(x => x.CuentaId);
            b.Ignore(x => x.Vacantes);
            b.Ignore(x => x.Asignaciones);
        });

        modelo.Entity<HorarioAtencion>(b =>
        {
            b.HasKey(x => x.HorarioId);
            b.Ignore(x => x.Cuenta);
        });

        modelo.Entity<Auditoria>(b => b.HasKey(x => x.AuditoriaId));
        modelo.Entity<ConfiguracionRegla>(b => b.HasKey(x => x.Clave));
    }
}
