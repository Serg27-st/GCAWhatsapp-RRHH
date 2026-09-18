using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// FUN-12: el conteo del aviso de retorno cruza la auditoría —que guarda el id de la conversación como
/// texto— con las conversaciones. En memoria eso siempre funciona; lo que hay que probar es que EF lo
/// traduzca a SQL en vez de fallar o traer todas las auditorías del período a memoria.
/// </summary>
public class AvisoRetornoAusenciaSqlTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    [FactConSqlServer]
    public async Task El_conteo_de_asignadas_por_ausencia_se_resuelve_en_la_base()
    {
        await using var db = sql.CrearContexto();

        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var titular = new Analista { Nombre = "Titular", Email = $"t.{sufijo}@gca.pe", Activo = true };
        var respaldo = new Analista { Nombre = "Respaldo", Email = $"r.{sufijo}@gca.pe", Activo = true };
        var cuenta = new Cuenta { Nombre = $"Cuenta {sufijo}", Activo = true };

        db.Analistas.AddRange(titular, respaldo);
        db.Cuentas.Add(cuenta);
        await db.SaveChangesAsync();

        var ahora = DateTime.UtcNow;

        Conversacion Hilo(int atiende) => new()
        {
            TelefonoE164 = $"+519{Random.Shared.Next(10000000, 99999999)}",
            Estado = EstadoConversacion.Activa,
            CuentaContextoId = cuenta.CuentaId,
            AnalistaAtendiendoId = atiende,
            FechaCreacion = ahora,
            FechaUltimaActividad = ahora
        };

        // Una sigue con el respaldo; la otra ya volvió al titular y no cuenta.
        var sigueConElRespaldo = Hilo(respaldo.AnalistaId);
        var yaVolvio = Hilo(titular.AnalistaId);

        db.Conversaciones.AddRange(sigueConElRespaldo, yaVolvio);
        await db.SaveChangesAsync();

        Auditoria Asignacion(Conversacion c, DateTime fecha) => new()
        {
            EntidadTipo = nameof(Conversacion),
            EntidadId = c.ConversacionId.ToString(),
            AnalistaId = respaldo.AnalistaId,
            Accion = "AsignacionPorAusencia",
            Fecha = fecha
        };

        db.Auditorias.AddRange(
            Asignacion(sigueConElRespaldo, ahora.AddDays(-2)),
            Asignacion(yaVolvio, ahora.AddDays(-2)),
            // Fuera del período de la ausencia: no es de esta.
            Asignacion(sigueConElRespaldo, ahora.AddDays(-30)));

        await db.SaveChangesAsync();

        var cantidad = await ServiciosDePrueba.Conversaciones(db).ContarAsignadasPorAusenciaAsync(
            [cuenta.CuentaId], titular.AnalistaId, ahora.AddDays(-7), ahora);

        Assert.Equal(1, cantidad);
    }
}
