using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Infrastructure.Proveedores;
using RRHH.WhatsApp.Tests.Proveedores;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// Regla 9 — primera mitad: elegida la empresa, el bot manda el formulario de la vacante.
/// </summary>
public class R09EnvioLinkTests : IDisposable
{
    private readonly EntornoDeReglas _entorno = new();

    private static Dictionary<string, string> SinCabeceras() => [];

    private IEnumerable<EnvioSimulado> Textos() => _entorno.Proveedor.Enviados.Where(e => e.Tipo == "texto");

    [Fact]
    public async Task Al_elegir_la_empresa_se_manda_el_enlace_de_su_unica_vacante()
    {
        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);
        await _entorno.ConsumirOutboxAsync();

        var envio = Assert.Single(Textos());

        Assert.Contains("Operario de produccion", envio.Detalle);
        Assert.Contains("https://forms.gle/operario", envio.Detalle);
    }

    [Fact]
    public async Task El_enlace_lleva_el_token_de_la_invitacion_y_no_el_HcId()
    {
        // Seccion 9.6.1: con el HcId en la URL se podrian enumerar vacantes de otras cuentas.
        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);
        await _entorno.ConsumirOutboxAsync();

        var invitacion = await _entorno.Db.JobFormsInvitaciones.AsNoTracking().FirstAsync();
        var envio = Assert.Single(Textos());

        Assert.Contains($"{EnlaceJobForms.Parametro}={invitacion.Token:N}", envio.Detalle);
    }

    [Fact]
    public async Task El_enlace_no_se_repite_con_cada_mensaje_del_postulante()
    {
        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);
        await _entorno.ConsumirOutboxAsync();

        // Un segundo mensaje sobre el mismo hilo y la misma cuenta no debe reenviar la ficha.
        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeLista);
        await _entorno.ConsumirOutboxAsync();

        Assert.Single(Textos());
        Assert.Single(_entorno.Db.JobFormsInvitaciones);
    }

    [Fact]
    public async Task Con_varias_vacantes_abiertas_el_bot_pregunta_a_cual_postular()
    {
        // El formulario y sus preguntas son por HC, no por cliente: sin elegir vacante no hay
        // enlace que mandar.
        _entorno.Db.Hcs.Add(new Hc
        {
            HcId = 2,
            CuentaId = EntornoDeReglas.CuentaId,
            Titulo = "Almacenero",
            Estado = EstadoHc.Abierta,
            UrlJobForms = "https://forms.gle/almacenero",
            FechaCreacion = _entorno.Ahora
        });

        await _entorno.Db.SaveChangesAsync();

        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);
        await _entorno.ConsumirOutboxAsync();

        Assert.Empty(Textos());
        Assert.Empty(_entorno.Db.JobFormsInvitaciones);

        var menu = Assert.Single(_entorno.Proveedor.Enviados, e => e.Tipo == "botones");

        Assert.Contains(IdsBoton.ParaVacante(1), menu.Detalle);
        Assert.Contains(IdsBoton.ParaVacante(2), menu.Detalle);
    }

    [Fact]
    public async Task Una_vacante_sin_formulario_cargado_no_manda_nada_y_queda_registrada()
    {
        // Es un dato de administracion que falta, no un error de codigo: tiene que verse.
        var vacante = await _entorno.Db.Hcs.FirstAsync();
        vacante.UrlJobForms = null;

        await _entorno.Db.SaveChangesAsync();

        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);
        await _entorno.ConsumirOutboxAsync();

        Assert.Empty(Textos());

        var aviso = await _entorno.Db.EventosSistema
            .AnyAsync(e => e.Tipo == TiposEvento.VacanteSinFormulario);

        Assert.True(aviso);
    }

    public void Dispose() => _entorno.Dispose();
}
