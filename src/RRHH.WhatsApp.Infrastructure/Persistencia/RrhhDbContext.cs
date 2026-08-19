using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;

namespace RRHH.WhatsApp.Infrastructure.Persistencia;

public class RrhhDbContext(DbContextOptions<RrhhDbContext> options) : DbContext(options)
{
    public DbSet<Cuenta> Cuentas => Set<Cuenta>();
    public DbSet<Analista> Analistas => Set<Analista>();
    public DbSet<AnalistaCuenta> AnalistaCuentas => Set<AnalistaCuenta>();
    public DbSet<Ausencia> Ausencias => Set<Ausencia>();
    public DbSet<HorarioAtencion> HorariosAtencion => Set<HorarioAtencion>();
    public DbSet<Hc> Hcs => Set<Hc>();
    public DbSet<HcCampoOpcional> HcCamposOpcionales => Set<HcCampoOpcional>();

    public DbSet<Postulante> Postulantes => Set<Postulante>();
    public DbSet<Postulacion> Postulaciones => Set<Postulacion>();
    public DbSet<EstadoPostulanteCuenta> EstadosPostulanteCuenta => Set<EstadoPostulanteCuenta>();
    public DbSet<EtapaKanban> EtapasKanban => Set<EtapaKanban>();

    public DbSet<Conversacion> Conversaciones => Set<Conversacion>();
    public DbSet<Mensaje> Mensajes => Set<Mensaje>();
    public DbSet<Transferencia> Transferencias => Set<Transferencia>();
    public DbSet<Plantilla> Plantillas => Set<Plantilla>();

    public DbSet<JobFormsInvitacion> JobFormsInvitaciones => Set<JobFormsInvitacion>();
    public DbSet<JobFormsRespuesta> JobFormsRespuestas => Set<JobFormsRespuesta>();

    public DbSet<EventoSistema> EventosSistema => Set<EventoSistema>();
    public DbSet<ConfiguracionRegla> ConfiguracionReglas => Set<ConfiguracionRegla>();
    public DbSet<Auditoria> Auditorias => Set<Auditoria>();

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        modelo.ApplyConfigurationsFromAssembly(typeof(RrhhDbContext).Assembly);
        DatosSemilla.Aplicar(modelo);
        base.OnModelCreating(modelo);
    }
}
