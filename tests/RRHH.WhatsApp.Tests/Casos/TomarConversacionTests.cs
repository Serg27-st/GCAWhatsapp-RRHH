using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Excepciones;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Tests.Proveedores;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// FUN-01 (AL1, A7, P3): «Sin clasificar» era una bandeja sin salida —se veía, pero nadie podía
/// adjudicarse un hilo—, así que las conversaciones se quedaban ahí sin dueño ni plazo.
/// </summary>
public class TomarConversacionTests : IDisposable
{
    private readonly EntornoDeReglas _entorno = new();

    /// <summary>Un hilo en «Sin clasificar», como lo deja la Regla 19 tras agotar el menú.</summary>
    private async Task<int> EnBandejaGeneralAsync()
    {
        await _entorno.IngresarAsync(PayloadsDePrueba.MensajeDeTexto);
        await _entorno.ConsumirOutboxAsync();

        var conversacion = await _entorno.Db.Conversaciones.FirstAsync();

        await _entorno.Conversaciones.DerivarAPendientesAsync(
            conversacion.ConversacionId, "El bot no reconocio la respuesta.");

        return conversacion.ConversacionId;
    }

    private Task<Conversacion> LeerAsync(int id) =>
        _entorno.Db.Conversaciones.AsNoTracking().FirstAsync(c => c.ConversacionId == id);

    [Fact]
    public async Task Tomar_deja_el_hilo_activo_con_su_cuenta_y_su_analista()
    {
        var id = await EnBandejaGeneralAsync();

        await _entorno.Conversaciones.TomarAsync(id, EntornoDeReglas.TitularId, EntornoDeReglas.CuentaId);

        var conversacion = await LeerAsync(id);

        Assert.Equal(EstadoConversacion.Activa, conversacion.Estado);
        Assert.Equal(EntornoDeReglas.TitularId, conversacion.AnalistaAtendiendoId);
        Assert.Equal(EntornoDeReglas.CuentaId, conversacion.CuentaContextoId);

        // Deja de correr el plazo de la bandeja general: ya tiene dueño (P3).
        Assert.Null(conversacion.FechaPendienteDesde);
        Assert.Contains(_entorno.Db.Auditorias, a => a.Accion == "TomadaDeBandejaGeneral");
    }

    [Fact]
    public async Task Tomarla_la_saca_de_la_bandeja_general_y_la_pone_en_la_del_analista()
    {
        var id = await EnBandejaGeneralAsync();

        await _entorno.Conversaciones.TomarAsync(id, EntornoDeReglas.TitularId, EntornoDeReglas.CuentaId);

        Assert.Empty(await _entorno.Conversaciones.ListarPendientesClasificarAsync());
        Assert.Contains(
            await _entorno.Conversaciones.ListarParaAnalistaAsync(EntornoDeReglas.TitularId),
            c => c.ConversacionId == id);
    }

    /// <summary>Regla 4: tomar es adjudicarse el hilo, y eso exige trabajar esa cuenta.</summary>
    [Fact]
    public async Task No_se_puede_tomar_para_una_cuenta_ajena()
    {
        var id = await EnBandejaGeneralAsync();

        _entorno.Db.Analistas.Add(new Analista
        {
            AnalistaId = 12, Nombre = "Rosa Diaz", Email = "rosa@gca.pe", Activo = true
        });

        await _entorno.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _entorno.Conversaciones.TomarAsync(id, 12, EntornoDeReglas.CuentaId));
    }

    [Fact]
    public async Task No_se_puede_tomar_una_conversacion_que_ya_atiende_alguien()
    {
        var id = await EnBandejaGeneralAsync();

        await _entorno.Conversaciones.TomarAsync(id, EntornoDeReglas.TitularId, EntornoDeReglas.CuentaId);

        var otra = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _entorno.Conversaciones.TomarAsync(id, EntornoDeReglas.RespaldoId, EntornoDeReglas.CuentaId));

        Assert.IsNotType<ConflictoConcurrenciaException>(otra);
        Assert.Contains("Sin clasificar", otra.Message);
    }

    /// <summary>FUN-01: responder sin tomar dejaba un hilo contestado que seguía figurando sin dueño.</summary>
    [Fact]
    public async Task Responder_sin_tomar_no_envia_nada()
    {
        var id = await EnBandejaGeneralAsync();
        var enviadosAntes = _entorno.Proveedor.Enviados.Count;

        var resultado = await _entorno.Envio.ResponderAsync(id, EntornoDeReglas.TitularId, "Hola", null, null);

        Assert.False(resultado.Enviado);
        Assert.Equal(MotivosBandeja.TomarPrimero, resultado.Motivo);
        Assert.Equal(enviadosAntes, _entorno.Proveedor.Enviados.Count);
    }

    [Fact]
    public async Task Transferir_sin_tomar_se_rechaza()
    {
        var id = await EnBandejaGeneralAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _entorno.Conversaciones.TransferirAsync(
                id, EntornoDeReglas.TitularId, EntornoDeReglas.RespaldoId, urgente: false, null));

        Assert.Equal(MotivosBandeja.TomarPrimero, error.Message);
    }

    [Fact]
    public async Task Despues_de_tomarla_el_analista_si_puede_responder()
    {
        var id = await EnBandejaGeneralAsync();

        await _entorno.Conversaciones.TomarAsync(id, EntornoDeReglas.TitularId, EntornoDeReglas.CuentaId);
        await _entorno.RefrescarActividadAsync(id);

        var resultado = await _entorno.Envio.ResponderAsync(id, EntornoDeReglas.TitularId, "Hola", null, null);

        Assert.True(resultado.Enviado);
    }

    public void Dispose() => _entorno.Dispose();
}
