using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Servicios;
using RRHH.WhatsApp.Tests.Proveedores;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// Regla 4 — cada analista ve solo sus conversaciones; Sistemas ve todo, pero ver no es atender.
/// <para>
/// El entorno trae una cuenta con titular y respaldo. Se suman un analista de Sistemas y uno que
/// no tiene nada que ver con esa cuenta, que es el caso que la regla existe para cubrir.
/// </para>
/// </summary>
public class VisibilidadTests : IDisposable
{
    private const int SistemasId = 12;
    private const int AjenoId = 13;
    private const string Dni = "45678912";

    private readonly EntornoDeReglas _entorno = new();
    private readonly CuentaService _cuentas;

    public VisibilidadTests()
    {
        _entorno.Db.Analistas.AddRange(
            new Analista
            {
                AnalistaId = SistemasId, Nombre = "Soporte", Email = "sistemas@gca.pe",
                Activo = true, Rol = RolAnalista.Sistemas
            },
            new Analista { AnalistaId = AjenoId, Nombre = "Otra Cuenta", Email = "otra@gca.pe", Activo = true });

        _entorno.Db.SaveChanges();

        _cuentas = new CuentaService(_entorno.Db);
    }

    private async Task<int> ConversacionAsignadaAsync()
    {
        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);
        await _entorno.ConsumirOutboxAsync();

        return (await _entorno.Db.Conversaciones.FirstAsync()).ConversacionId;
    }

    private Task<NivelAcceso> AccesoAsync(int conversacionId, int analistaId) =>
        _entorno.Conversaciones.ObtenerAccesoAsync(conversacionId, analistaId);

    [Fact]
    public async Task Quien_la_atiende_la_trabaja()
    {
        var id = await ConversacionAsignadaAsync();

        Assert.Equal(NivelAcceso.Total, await AccesoAsync(id, EntornoDeReglas.TitularId));
    }

    /// <summary>Ni siquiera el respaldo de la cuenta: la ve cuando la Regla 2 se la pasa, no antes.</summary>
    [Fact]
    public async Task Otro_analista_no_la_ve()
    {
        var id = await ConversacionAsignadaAsync();

        Assert.Equal(NivelAcceso.Ninguno, await AccesoAsync(id, EntornoDeReglas.RespaldoId));
        Assert.Equal(NivelAcceso.Ninguno, await AccesoAsync(id, AjenoId));
    }

    [Fact]
    public async Task Sistemas_la_ve_pero_no_la_trabaja()
    {
        var id = await ConversacionAsignadaAsync();

        Assert.Equal(NivelAcceso.Lectura, await AccesoAsync(id, SistemasId));
    }

    /// <summary>Regla 19: la bandeja general es de todos, y tomar un hilo exige poder responderle.</summary>
    [Fact]
    public async Task Lo_sin_clasificar_es_de_todos()
    {
        var id = await ConversacionAsignadaAsync();
        await _entorno.Conversaciones.CambiarEstadoAsync(id, EstadoConversacion.PendienteClasificar);

        Assert.Equal(NivelAcceso.Total, await AccesoAsync(id, AjenoId));
    }

    [Fact]
    public async Task Un_id_que_no_existe_no_da_acceso()
    {
        Assert.Equal(NivelAcceso.Ninguno, await AccesoAsync(9999, EntornoDeReglas.TitularId));
    }

    /// <summary>Regla 8 con la 4: el hilo cambia de manos al aceptar, no al pedir.</summary>
    [Fact]
    public async Task Una_transferencia_pendiente_no_le_abre_el_hilo_al_destino()
    {
        var id = await ConversacionAsignadaAsync();

        await _entorno.Conversaciones.TransferirAsync(
            id, EntornoDeReglas.TitularId, EntornoDeReglas.RespaldoId, urgente: false, null);

        Assert.Equal(NivelAcceso.Ninguno, await AccesoAsync(id, EntornoDeReglas.RespaldoId));
        Assert.Equal(NivelAcceso.Total, await AccesoAsync(id, EntornoDeReglas.TitularId));
    }

    [Fact]
    public async Task Al_aceptarla_el_hilo_cambia_de_manos()
    {
        var id = await ConversacionAsignadaAsync();

        var transferencia = await _entorno.Conversaciones.TransferirAsync(
            id, EntornoDeReglas.TitularId, EntornoDeReglas.RespaldoId, urgente: false, null);

        await _entorno.Conversaciones.ResponderTransferenciaAsync(
            transferencia.TransferenciaId, EntornoDeReglas.RespaldoId, aceptada: true);

        Assert.Equal(NivelAcceso.Total, await AccesoAsync(id, EntornoDeReglas.RespaldoId));
        Assert.Equal(NivelAcceso.Ninguno, await AccesoAsync(id, EntornoDeReglas.TitularId));
    }

    /// <summary>Reglas 4 y 6: el buscador trae el chat propio, no el de otra cuenta.</summary>
    [Fact]
    public async Task El_buscador_por_dni_no_cruza_cuentas()
    {
        var id = await ConversacionAsignadaAsync();

        var postulante = await _entorno.Postulantes.RegistrarDesdeFormularioAsync(
            new DatosPostulanteFormulario(Dni, "Maria Quispe", null, null));

        await _entorno.Conversaciones.VincularPostulanteAsync(id, postulante.PostulanteId);

        Assert.Single(await _entorno.Conversaciones.BuscarPorDniAsync(Dni, EntornoDeReglas.TitularId));
        Assert.Empty(await _entorno.Conversaciones.BuscarPorDniAsync(Dni, AjenoId));
        Assert.Single(await _entorno.Conversaciones.BuscarPorDniAsync(Dni, SistemasId));
    }

    /// <summary>El tablero es por cuenta: el respaldo lo sigue porque la Regla 2 le pasa las conversaciones.</summary>
    [Fact]
    public async Task El_titular_y_el_respaldo_trabajan_el_tablero_de_su_cuenta()
    {
        Assert.Equal(NivelAcceso.Total,
            await _cuentas.ObtenerAccesoAsync(EntornoDeReglas.CuentaId, EntornoDeReglas.TitularId));

        Assert.Equal(NivelAcceso.Total,
            await _cuentas.ObtenerAccesoAsync(EntornoDeReglas.CuentaId, EntornoDeReglas.RespaldoId));
    }

    [Fact]
    public async Task Un_analista_de_otra_cuenta_no_ve_el_tablero()
    {
        Assert.Equal(NivelAcceso.Ninguno,
            await _cuentas.ObtenerAccesoAsync(EntornoDeReglas.CuentaId, AjenoId));
    }

    [Fact]
    public async Task Sistemas_ve_el_tablero_sin_mover_tarjetas()
    {
        Assert.Equal(NivelAcceso.Lectura,
            await _cuentas.ObtenerAccesoAsync(EntornoDeReglas.CuentaId, SistemasId));
    }

    public void Dispose() => _entorno.Dispose();
}
