using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RRHH.WhatsApp.Domain.Entidades;

namespace RRHH.WhatsApp.Infrastructure.Persistencia;

// Nota general sobre DeleteBehavior.Restrict: SQL Server rechaza multiples rutas de borrado en
// cascada hacia la misma tabla, y Postulacion apunta a Postulante, Hc, Cuenta, Analista y
// EtapaKanban a la vez. Ademas, ninguna de estas filas deberia borrarse en la practica: las bajas
// se hacen con Activo = false o con anonimizacion (Regla 17).

public class CuentaConfig : IEntityTypeConfiguration<Cuenta>
{
    public void Configure(EntityTypeBuilder<Cuenta> b)
    {
        b.ToTable("Cuentas");
        b.HasKey(x => x.CuentaId);
        b.Property(x => x.Nombre).HasMaxLength(150).IsRequired();
        b.HasIndex(x => x.Nombre).IsUnique();
    }
}

public class AnalistaConfig : IEntityTypeConfiguration<Analista>
{
    public void Configure(EntityTypeBuilder<Analista> b)
    {
        b.ToTable("Analistas");
        b.HasKey(x => x.AnalistaId);
        b.Property(x => x.Nombre).HasMaxLength(150).IsRequired();
        b.Property(x => x.Email).HasMaxLength(200).IsRequired();
        b.Property(x => x.Rol).HasConversion<int>();
        b.Property(x => x.HashContrasena).HasMaxLength(400);

        // ARQ-11 (V34): los analistas que ya existen arrancan en 1, como los nuevos; lo que importa es
        // que el token traiga la misma version que la fila, no el numero en si.
        b.Property(x => x.VersionSeguridad).HasDefaultValue(1);

        b.HasIndex(x => x.Email).IsUnique();
    }
}

public class AnalistaCuentaConfig : IEntityTypeConfiguration<AnalistaCuenta>
{
    public void Configure(EntityTypeBuilder<AnalistaCuenta> b)
    {
        b.ToTable("AnalistaCuenta");
        b.HasKey(x => x.AnalistaCuentaId);

        // Un analista no puede figurar dos veces en la misma cuenta.
        b.HasIndex(x => new { x.AnalistaId, x.CuentaId }).IsUnique();

        // Regla 2: el respaldo es fijo por cuenta, asi que solo puede haber uno.
        b.HasIndex(x => x.CuentaId, "IX_AnalistaCuenta_RespaldoUnicoPorCuenta")
            .HasFilter("[EsBackup] = 1")
            .IsUnique();

        // Regla 1: el titular tambien es uno solo por cuenta. Sin este indice, dos titulares
        // dejarian el enrutamiento eligiendo uno de forma arbitraria, y el que quedara afuera
        // nunca veria las conversaciones de su cuenta.
        b.HasIndex(x => x.CuentaId, "IX_AnalistaCuenta_TitularUnicoPorCuenta")
            .HasFilter("[EsBackup] = 0")
            .IsUnique();

        b.HasOne(x => x.Analista).WithMany(x => x.Cuentas)
            .HasForeignKey(x => x.AnalistaId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Cuenta).WithMany(x => x.Asignaciones)
            .HasForeignKey(x => x.CuentaId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AusenciaConfig : IEntityTypeConfiguration<Ausencia>
{
    public void Configure(EntityTypeBuilder<Ausencia> b)
    {
        b.ToTable("Ausencias");
        b.HasKey(x => x.AusenciaId);
        b.Property(x => x.Motivo).HasMaxLength(300);

        // Consulta caliente de la Regla 14: se evalua antes de asignar cada conversacion.
        b.HasIndex(x => new { x.AnalistaId, x.FechaInicio, x.FechaFin });

        b.HasOne(x => x.Analista).WithMany(x => x.Ausencias)
            .HasForeignKey(x => x.AnalistaId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class HorarioAtencionConfig : IEntityTypeConfiguration<HorarioAtencion>
{
    public void Configure(EntityTypeBuilder<HorarioAtencion> b)
    {
        b.ToTable("HorarioAtencion");
        b.HasKey(x => x.HorarioId);
        b.Property(x => x.DiaSemana).HasConversion<int>();
        b.HasIndex(x => new { x.CuentaId, x.DiaSemana });

        b.HasOne(x => x.Cuenta).WithMany()
            .HasForeignKey(x => x.CuentaId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class HcConfig : IEntityTypeConfiguration<Hc>
{
    public void Configure(EntityTypeBuilder<Hc> b)
    {
        b.ToTable("HC");
        b.HasKey(x => x.HcId);
        b.Property(x => x.Titulo).HasMaxLength(200).IsRequired();
        b.Property(x => x.Estado).HasConversion<int>();
        b.Property(x => x.CodigoJobForms).HasMaxLength(200);
        b.Property(x => x.UrlJobForms).HasMaxLength(500);

        // Regla 20: el bot filtra vacantes abiertas por cuenta en cada menu.
        b.HasIndex(x => new { x.CuentaId, x.Estado });

        // A6: el codigo del aviso identifica la vacante desde el primer mensaje, asi que no puede repetirse.
        b.Property(x => x.CodigoAviso).HasMaxLength(12);
        b.HasIndex(x => x.CodigoAviso, "IX_HC_CodigoAviso")
            .IsUnique()
            .HasFilter("[CodigoAviso] IS NOT NULL");

        b.HasOne(x => x.Cuenta).WithMany(x => x.Vacantes)
            .HasForeignKey(x => x.CuentaId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class HcCampoOpcionalConfig : IEntityTypeConfiguration<HcCampoOpcional>
{
    public void Configure(EntityTypeBuilder<HcCampoOpcional> b)
    {
        b.ToTable("HCCamposOpcionales");
        b.HasKey(x => x.CampoId);
        b.Property(x => x.NombreCampo).HasMaxLength(150).IsRequired();
        b.Property(x => x.Tipo).HasConversion<int>();

        b.HasOne(x => x.Hc).WithMany(x => x.CamposOpcionales)
            .HasForeignKey(x => x.HcId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class PostulanteConfig : IEntityTypeConfiguration<Postulante>
{
    public void Configure(EntityTypeBuilder<Postulante> b)
    {
        b.ToTable("Postulantes");
        b.HasKey(x => x.PostulanteId);

        // Regla 9: el DNI es el identificador principal, no el numero de WhatsApp.
        b.Property(x => x.Dni).HasMaxLength(20).IsRequired();
        b.HasIndex(x => x.Dni).IsUnique();

        b.Property(x => x.NombreCompleto).HasMaxLength(200);
        b.Property(x => x.TelefonoUltimo).HasMaxLength(20);
        b.Property(x => x.Email).HasMaxLength(200);
    }
}

public class PostulacionConfig : IEntityTypeConfiguration<Postulacion>
{
    public void Configure(EntityTypeBuilder<Postulacion> b)
    {
        b.ToTable("Postulaciones");
        b.HasKey(x => x.PostulacionId);
        b.Property(x => x.Estado).HasConversion<int>();
        b.Property(x => x.RowVersion).IsRowVersion();

        // Un postulante no se duplica dentro de la misma vacante.
        b.HasIndex(x => new { x.PostulanteId, x.HcId }).IsUnique();

        // Regla 13: carga del tablero por vacante.
        b.HasIndex(x => new { x.HcId, x.EtapaKanbanId });

        // Bandeja del analista filtrada por cuenta (Reglas 1 y 4).
        b.HasIndex(x => new { x.AnalistaAsignadoId, x.CuentaId, x.Estado });

        // Regla 16: barrido de archivado a los 90 dias.
        b.HasIndex(x => new { x.Estado, x.FechaUltimaActividad });

        b.HasOne(x => x.Postulante).WithMany(x => x.Postulaciones)
            .HasForeignKey(x => x.PostulanteId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Hc).WithMany()
            .HasForeignKey(x => x.HcId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Cuenta).WithMany()
            .HasForeignKey(x => x.CuentaId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.AnalistaAsignado).WithMany()
            .HasForeignKey(x => x.AnalistaAsignadoId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.EtapaKanban).WithMany()
            .HasForeignKey(x => x.EtapaKanbanId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class EstadoPostulanteCuentaConfig : IEntityTypeConfiguration<EstadoPostulanteCuenta>
{
    public void Configure(EntityTypeBuilder<EstadoPostulanteCuenta> b)
    {
        b.ToTable("EstadosPostulanteCuenta");
        b.HasKey(x => x.EstadoId);
        b.Property(x => x.Tipo).HasConversion<int>();
        b.Property(x => x.Motivo).HasMaxLength(500);
        b.HasIndex(x => new { x.PostulanteId, x.CuentaId, x.Tipo });

        b.HasOne(x => x.Postulante).WithMany(x => x.Estados)
            .HasForeignKey(x => x.PostulanteId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Cuenta).WithMany()
            .HasForeignKey(x => x.CuentaId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Analista).WithMany()
            .HasForeignKey(x => x.AnalistaId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class EtapaKanbanConfig : IEntityTypeConfiguration<EtapaKanban>
{
    public void Configure(EntityTypeBuilder<EtapaKanban> b)
    {
        b.ToTable("EtapasKanban");
        b.HasKey(x => x.EtapaId);
        b.Property(x => x.Nombre).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Orden).IsUnique();
        b.Property(x => x.EstadoResultante).HasConversion<int?>();
    }
}

public class ConversacionConfig : IEntityTypeConfiguration<Conversacion>
{
    public void Configure(EntityTypeBuilder<Conversacion> b)
    {
        b.ToTable("Conversaciones");
        b.HasKey(x => x.ConversacionId);

        // El telefono es la clave natural del hilo: WhatsApp entrega los mensajes por numero.
        b.Property(x => x.TelefonoE164).HasMaxLength(20).IsRequired();
        b.HasIndex(x => x.TelefonoE164).IsUnique();

        b.Property(x => x.Estado).HasConversion<int>();
        b.Property(x => x.OrigenOptIn).HasConversion<int>();
        b.Property(x => x.RowVersion).IsRowVersion();

        // Regla 2: barrido de escalamiento del Worker.
        b.HasIndex(x => new { x.Estado, x.FechaUltimaRespuestaAnalista });

        // Bandeja del analista.
        b.HasIndex(x => new { x.AnalistaAtendiendoId, x.Estado });

        // Barridos por estado y antigüedad (V30): el plazo de «Sin clasificar», la derivacion por
        // silencio del menu y el archivado miran el estado y cuanto hace que no hay actividad.
        b.HasIndex(x => new { x.Estado, x.FechaUltimaActividad }, "IX_Conversaciones_EstadoActividad");

        b.HasOne(x => x.Postulante).WithMany()
            .HasForeignKey(x => x.PostulanteId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CuentaContexto).WithMany()
            .HasForeignKey(x => x.CuentaContextoId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.AnalistaAtendiendo).WithMany()
            .HasForeignKey(x => x.AnalistaAtendiendoId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class MensajeConfig : IEntityTypeConfiguration<Mensaje>
{
    public void Configure(EntityTypeBuilder<Mensaje> b)
    {
        b.ToTable("Mensajes");
        b.HasKey(x => x.MensajeId);
        b.Property(x => x.Direccion).HasConversion<int>();
        b.Property(x => x.EstadoEntrega).HasConversion<int>();
        b.Property(x => x.Contenido).HasMaxLength(4096).IsRequired();
        b.Property(x => x.ProviderMessageId).HasMaxLength(150);
        b.Property(x => x.ErrorProveedor).HasMaxLength(500);
        b.Property(x => x.ParametrosPlantillaJson).HasMaxLength(2000);

        // Idempotencia (Seccion 9.6.2): Meta reintenta la entrega y no debemos procesar dos veces.
        // El filtro permite que los salientes aun sin id del proveedor convivan como NULL.
        b.HasIndex(x => x.ProviderMessageId)
            .IsUnique()
            .HasFilter("[ProviderMessageId] IS NOT NULL");

        b.HasIndex(x => new { x.ConversacionId, x.FechaEnvio });
        b.HasIndex(x => x.CorrelationId);

        b.Property(x => x.ClaseFallo).HasConversion<int>();

        // Barrido de reintentos del Worker: solo mira lo fallido con un intento ya vencido, asi
        // que el indice se filtra para no cargar el historico entero de mensajes.
        b.HasIndex(x => new { x.EstadoEntrega, x.ClaseFallo, x.ProximoIntentoUtc })
            .HasFilter("[ProximoIntentoUtc] IS NOT NULL")
            .HasDatabaseName("IX_Mensajes_PendientesDeReintento");

        // Cola de envios (V29). Nombres explicitos en los indices: EF identifica un indice por sus
        // columnas, y dos sobre las mismas colisionan (V17 documento una migracion que borraba uno).
        b.Property(x => x.TipoSaliente).HasConversion<int>().HasDefaultValue(Domain.Enums.TipoSaliente.Texto);
        b.Property(x => x.OpcionesJson).HasColumnType("nvarchar(max)");
        b.Property(x => x.ClaveIdempotencia).HasMaxLength(150);
        b.Property(x => x.FechaTomaEnvio);

        // La garantia real de no duplicar: el mismo envio decidido dos veces choca aca.
        b.HasIndex(x => x.ClaveIdempotencia, "IX_Mensajes_ClaveIdempotencia")
            .IsUnique()
            .HasFilter("[ClaveIdempotencia] IS NOT NULL");

        // El despachador solo mira lo encolado, en orden de llegada.
        b.HasIndex(x => new { x.EstadoEntrega, x.FechaEnvio }, "IX_Mensajes_EnCola")
            .HasFilter("[EstadoEntrega] = 6");

        b.HasOne(x => x.Conversacion).WithMany(x => x.Mensajes)
            .HasForeignKey(x => x.ConversacionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Plantilla).WithMany()
            .HasForeignKey(x => x.PlantillaId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Analista).WithMany()
            .HasForeignKey(x => x.AnalistaId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class MensajeAdjuntoConfig : IEntityTypeConfiguration<MensajeAdjunto>
{
    public void Configure(EntityTypeBuilder<MensajeAdjunto> b)
    {
        b.ToTable("MensajesAdjuntos");
        b.HasKey(x => x.AdjuntoId);
        b.Property(x => x.TipoMedio).HasMaxLength(20).IsRequired();
        b.Property(x => x.ProveedorMedioId).HasMaxLength(150).IsRequired();
        b.Property(x => x.MimeType).HasMaxLength(100).IsRequired();
        b.Property(x => x.NombreArchivo).HasMaxLength(255);
        b.Property(x => x.Ruta).HasMaxLength(500);
        b.Property(x => x.Error).HasMaxLength(500);
        b.Property(x => x.Estado).HasConversion<int>();

        // V33: el Worker baja lo pendiente en orden de llegada —el id de medio caduca— y la purga de la
        // Regla 17 recorre lo descargado por fecha. Las dos preguntas empiezan por el estado.
        b.HasIndex(x => new { x.Estado, x.FechaRecepcion }, "IX_MensajesAdjuntos_Estado_FechaRecepcion");

        // Un adjunto no existe sin su mensaje: si el mensaje se borra, el archivo no puede quedar
        // registrado sin dueño y fuera del alcance de la purga.
        b.HasOne(x => x.Mensaje).WithMany(x => x.Adjuntos)
            .HasForeignKey(x => x.MensajeId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class TransferenciaConfig : IEntityTypeConfiguration<Transferencia>
{
    public void Configure(EntityTypeBuilder<Transferencia> b)
    {
        b.ToTable("Transferencias");
        b.HasKey(x => x.TransferenciaId);
        b.Property(x => x.Estado).HasConversion<int>();
        b.Property(x => x.Comentario).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsRowVersion();

        // Regla 8: bandeja de transferencias pendientes de aceptar.
        b.HasIndex(x => new { x.AnalistaDestinoId, x.Estado });

        // Regla 8, de uno en uno: una sola pendiente por conversacion. La comprobacion del servicio no ve
        // dos pedidos simultaneos; el indice si.
        b.HasIndex(x => x.ConversacionId, "IX_Transferencias_PendienteUnica")
            .IsUnique()
            .HasFilter("[Estado] = 1");

        // El indice de la clave foranea, sin filtro, con nombre explicito. Sin declararlo, EF toma el
        // filtrado de arriba como indice de la FK y borra este en la migracion: las consultas por
        // conversacion en cualquier otro estado, y el borrado en cascada, se quedarian sin indice (V17).
        b.HasIndex(x => x.ConversacionId, "IX_Transferencias_ConversacionId");

        b.HasOne(x => x.Conversacion).WithMany()
            .HasForeignKey(x => x.ConversacionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Postulacion).WithMany()
            .HasForeignKey(x => x.PostulacionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.AnalistaOrigen).WithMany()
            .HasForeignKey(x => x.AnalistaOrigenId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.AnalistaDestino).WithMany()
            .HasForeignKey(x => x.AnalistaDestinoId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PlantillaConfig : IEntityTypeConfiguration<Plantilla>
{
    public void Configure(EntityTypeBuilder<Plantilla> b)
    {
        b.ToTable("Plantillas");
        b.HasKey(x => x.PlantillaId);
        b.Property(x => x.Clave).HasMaxLength(100).IsRequired();
        b.Property(x => x.NombreMeta).HasMaxLength(150).IsRequired();
        b.Property(x => x.Idioma).HasMaxLength(10).IsRequired();
        b.Property(x => x.TextoAprobado).HasMaxLength(2000).IsRequired();
        b.Property(x => x.Categoria).HasConversion<int>();
        b.HasIndex(x => x.Clave).IsUnique();
    }
}

public class JobFormsInvitacionConfig : IEntityTypeConfiguration<JobFormsInvitacion>
{
    public void Configure(EntityTypeBuilder<JobFormsInvitacion> b)
    {
        b.ToTable("JobFormsInvitaciones");
        b.HasKey(x => x.InvitacionId);

        // Seccion 9.6.1: el token viaja en la URL en lugar del HcId secuencial.
        b.HasIndex(x => x.Token).IsUnique();

        // Regla 9: barridos del Worker para el recordatorio de 24h y el aviso de 48h.
        b.HasIndex(x => new { x.Completado, x.RecordatorioEnviado, x.FechaEnvioLink });
        b.HasIndex(x => new { x.Completado, x.AvisoAnalistaEnviado, x.FechaEnvioLink });

        b.HasOne(x => x.Conversacion).WithMany()
            .HasForeignKey(x => x.ConversacionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Postulante).WithMany()
            .HasForeignKey(x => x.PostulanteId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Hc).WithMany()
            .HasForeignKey(x => x.HcId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class JobFormsRespuestaConfig : IEntityTypeConfiguration<JobFormsRespuesta>
{
    public void Configure(EntityTypeBuilder<JobFormsRespuesta> b)
    {
        b.ToTable("JobFormsRespuestas");
        b.HasKey(x => x.RespuestaId);
        b.Property(x => x.DatosJson).IsRequired();
        b.Property(x => x.CvUrl).HasMaxLength(500);
        b.Property(x => x.VersionAvisoPrivacidad).HasMaxLength(50);
        b.HasIndex(x => new { x.PostulanteId, x.HcId });

        // B9: una respuesta por invitacion. La idempotencia del envio (V27) lo resolvia en el servicio,
        // pero dos reintentos simultaneos del Apps Script podian guardar dos. El filtro deja convivir
        // las respuestas sin invitacion.
        b.HasIndex(x => x.InvitacionId, "IX_JobFormsRespuestas_Invitacion")
            .IsUnique()
            .HasFilter("[InvitacionId] IS NOT NULL");

        b.HasOne(x => x.Invitacion).WithMany()
            .HasForeignKey(x => x.InvitacionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Postulante).WithMany()
            .HasForeignKey(x => x.PostulanteId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Hc).WithMany()
            .HasForeignKey(x => x.HcId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class EventoSistemaConfig : IEntityTypeConfiguration<EventoSistema>
{
    public void Configure(EntityTypeBuilder<EventoSistema> b)
    {
        b.ToTable("EventosSistema");
        b.HasKey(x => x.EventoId);
        b.Property(x => x.Tipo).HasMaxLength(100).IsRequired();
        b.Property(x => x.Payload).IsRequired();
        b.Property(x => x.Estado).HasConversion<int>();
        b.Property(x => x.UltimoError).HasMaxLength(2000);

        // Consulta principal del Worker sobre la outbox (ARQ-13). Incluye el tipo porque cada consumidor
        // pide solo los suyos (V9): sin el, la consulta recorria los pendientes de todos los tipos.
        b.HasIndex(x => new { x.Estado, x.Tipo, x.FechaCreacion }, "IX_EventosSistema_Cola");
        b.HasIndex(x => x.CorrelationId);
    }
}

public class ConfiguracionReglaConfig : IEntityTypeConfiguration<ConfiguracionRegla>
{
    public void Configure(EntityTypeBuilder<ConfiguracionRegla> b)
    {
        b.ToTable("ConfiguracionReglas");
        b.HasKey(x => x.Clave);
        b.Property(x => x.Clave).HasMaxLength(100);
        b.Property(x => x.Valor).HasMaxLength(500).IsRequired();
        b.Property(x => x.Descripcion).HasMaxLength(500);
    }
}

public class AlertaOperativaConfig : IEntityTypeConfiguration<AlertaOperativa>
{
    public void Configure(EntityTypeBuilder<AlertaOperativa> b)
    {
        b.ToTable("AlertasOperativas");
        b.HasKey(x => x.AlertaId);
        b.Property(x => x.Tipo).HasMaxLength(60).IsRequired();
        b.Property(x => x.Clave).HasMaxLength(150).IsRequired();
        b.Property(x => x.Detalle).HasMaxLength(1000);

        // V32: una sola alerta abierta por (Tipo, Clave). Las resueltas quedan como historial y no
        // cuentan: si el problema vuelve, es una alerta nueva.
        b.HasIndex(x => new { x.Tipo, x.Clave }, "IX_AlertasOperativas_AbiertaUnica")
            .IsUnique()
            .HasFilter("[FechaResuelta] IS NULL");

        b.HasOne<Analista>().WithMany()
            .HasForeignKey(x => x.ResueltaPorAnalistaId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AuditoriaConfig : IEntityTypeConfiguration<Auditoria>
{
    public void Configure(EntityTypeBuilder<Auditoria> b)
    {
        b.ToTable("Auditoria");
        b.HasKey(x => x.AuditoriaId);
        b.Property(x => x.EntidadTipo).HasMaxLength(100).IsRequired();
        b.Property(x => x.EntidadId).HasMaxLength(50).IsRequired();
        b.Property(x => x.Accion).HasMaxLength(100).IsRequired();
        b.Property(x => x.Detalle).HasMaxLength(2000);
        b.HasIndex(x => new { x.EntidadTipo, x.EntidadId, x.Fecha });
    }
}

/// <summary>
/// Señal de vida de los bucles del Worker (Sección 9.6.2). Una fila por bucle: el nombre es la
/// clave porque no interesa el historial, solo el último latido.
/// </summary>
public class LatidoServicioConfig : IEntityTypeConfiguration<LatidoServicio>
{
    public void Configure(EntityTypeBuilder<LatidoServicio> b)
    {
        b.ToTable("LatidosServicio");
        b.HasKey(x => x.Servicio);
        b.Property(x => x.Servicio).HasMaxLength(80);
        b.Property(x => x.Detalle).HasMaxLength(300);
    }
}
