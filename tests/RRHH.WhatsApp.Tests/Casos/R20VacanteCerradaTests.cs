using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Infrastructure.Proveedores;
using RRHH.WhatsApp.Tests.Proveedores;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// Regla 20 — vacante cerrada. Lo que no puede pasar es que el postulante reciba el enlace de una
/// vacante ya cubierta y llene un formulario que no lleva a ninguna parte.
/// </summary>
public class R20VacanteCerradaTests : IDisposable
{
    private readonly EntornoDeReglas _entorno = new();

    private IEnumerable<EnvioSimulado> Textos() => _entorno.Proveedor.Enviados.Where(e => e.Tipo == "texto");

    private async Task CerrarVacanteAsync(int hcId)
    {
        var vacante = await _entorno.Db.Hcs.FirstAsync(h => h.HcId == hcId);

        vacante.Estado = EstadoHc.Cerrada;
        vacante.FechaCierre = _entorno.Ahora;

        await _entorno.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Sin_vacantes_abiertas_se_avisa_y_se_vuelve_al_menu_de_empresas()
    {
        await CerrarVacanteAsync(1);

        // Se deja otra cuenta lista para el menu: activa, con titular y con vacante con formulario
        // (COR-09). Sin cualquiera de las tres el menu no la ofreceria.
        _entorno.Db.Cuentas.Add(new Cuenta { CuentaId = 8, Nombre = "Intradevco", Activo = true });
        _entorno.Db.AnalistaCuentas.Add(new AnalistaCuenta
        {
            AnalistaCuentaId = 3, AnalistaId = EntornoDeReglas.TitularId, CuentaId = 8, EsBackup = false
        });
        _entorno.Db.Hcs.Add(new Hc
        {
            HcId = 3,
            CuentaId = 8,
            Titulo = "Envasador",
            Estado = EstadoHc.Abierta,
            UrlJobForms = "https://forms.gle/envasador",
            FechaCreacion = _entorno.Ahora
        });

        await _entorno.Db.SaveChangesAsync();

        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);
        await _entorno.ConsumirOutboxAsync();

        // COR-03 (P1): el postulante acaba de escribir, asi que el aviso sale en texto y no depende
        // de que Meta haya aprobado la plantilla, que sigue inactiva.
        var aviso = Assert.Single(Textos());
        Assert.Contains("ya fue cubierta", aviso.Detalle);

        Assert.Single(_entorno.Proveedor.Enviados, e => e.Tipo == "botones");

        // La cuenta deja de servir de contexto: sin vacantes no hay a que postular ahi.
        var conversacion = await _entorno.Db.Conversaciones.FirstAsync();
        Assert.Null(conversacion.CuentaContextoId);
    }

    [Fact]
    public async Task No_se_manda_el_enlace_de_una_vacante_cubierta()
    {
        await CerrarVacanteAsync(1);

        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);
        await _entorno.ConsumirOutboxAsync();

        // Sale el aviso de la vacante cubierta, pero ningun enlace de formulario.
        Assert.DoesNotContain(Textos(), e => e.Detalle.Contains("completa esta ficha"));
        Assert.Empty(_entorno.Db.JobFormsInvitaciones);
    }

    [Fact]
    public async Task Si_quedan_otras_vacantes_de_la_cuenta_se_ofrecen_esas()
    {
        // El dossier lo pide explicito: mostrar de nuevo las opciones activas de la misma cuenta.
        _entorno.Db.Hcs.Add(new Hc
        {
            HcId = 42,
            CuentaId = EntornoDeReglas.CuentaId,
            Titulo = "Vacante vieja",
            Estado = EstadoHc.Cerrada,
            FechaCreacion = _entorno.Ahora
        });

        await _entorno.Db.SaveChangesAsync();

        // El payload de lista elige justamente la vacante 42, que esta cerrada.
        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeLista);
        await _entorno.ConsumirOutboxAsync();

        var menu = Assert.Single(_entorno.Proveedor.Enviados, e => e.Tipo == "botones");

        Assert.Contains(IdsBoton.ParaVacante(1), menu.Detalle);
        Assert.DoesNotContain(IdsBoton.ParaVacante(42), menu.Detalle);

        // La cuenta se conserva: sigue teniendo que ofrecer.
        var conversacion = await _entorno.Db.Conversaciones.FirstAsync();
        Assert.Equal(EntornoDeReglas.CuentaId, conversacion.CuentaContextoId);
    }

    [Fact]
    public async Task El_aviso_queda_registrado_en_la_auditoria()
    {
        await CerrarVacanteAsync(1);

        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);
        await _entorno.ConsumirOutboxAsync();

        var auditoria = await _entorno.Db.Auditorias
            .AnyAsync(a => a.Accion == "VacanteCerrada");

        Assert.True(auditoria);
    }

    public void Dispose() => _entorno.Dispose();
}
