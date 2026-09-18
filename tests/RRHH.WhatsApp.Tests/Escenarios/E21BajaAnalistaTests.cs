using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Tests.Casos;

namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// E21 (FUN-19): alguien deja la empresa. Su bandeja no puede quedar abandonada: sus conversaciones
/// pasan al respaldo de cada cuenta, sus transferencias se resuelven y su token deja de servir.
/// <para>
/// Antes no había forma de darlo de baja: quedaba activo o se le borraba la fila, y con ella el
/// historial que sostiene las métricas de la Regla 18.
/// </para>
/// </summary>
public class E21BajaAnalistaTests : IDisposable
{
    private const int SistemasId = 99;

    private readonly ArnesEscenario _arnes = new();

    private EntornoDeReglas Entorno => _arnes.Entorno;

    public E21BajaAnalistaTests()
    {
        Entorno.Db.Analistas.Add(new Analista
        {
            AnalistaId = SistemasId, Nombre = "Soporte", Email = "sistemas@gca.pe",
            Rol = RolAnalista.Sistemas, Activo = true
        });

        Entorno.Db.SaveChanges();
    }

    /// <summary>Deja un hilo asignado al titular de la cuenta, con su postulante escribiendo.</summary>
    private async Task<int> HiloAsignadoAsync(string telefono)
    {
        await _arnes.ConversarAsync(
            new Entrante("Hola", telefono),
            new ConsumirOutbox(),
            new Boton("cuenta_7", "Alicorp", telefono),
            new ConsumirOutbox());

        var hilo = await _arnes.ConversacionAsync(telefono);

        Assert.Equal(EntornoDeReglas.TitularId, hilo.AnalistaAtendiendoId);

        return hilo.ConversacionId;
    }

    private Task<Conversacion> HiloAsync(int id) =>
        Entorno.Db.Conversaciones.AsNoTracking().FirstAsync(c => c.ConversacionId == id);

    private Task<Analista> AnalistaAsync(int id) =>
        Entorno.Db.Analistas.AsNoTracking().FirstAsync(a => a.AnalistaId == id);

    [Fact]
    public async Task Al_dar_de_baja_al_titular_sus_conversaciones_pasan_al_respaldo()
    {
        var uno = await HiloAsignadoAsync("+51900000001");
        var dos = await HiloAsignadoAsync("+51900000002");

        var cartera = await Entorno.Analistas.ObtenerCarteraAsync(EntornoDeReglas.TitularId);

        Assert.Equal(2, cartera.Conversaciones);
        Assert.Equal(1, cartera.CuentasTitular);
        Assert.Equal(0, cartera.CuentasRespaldo);

        var antes = (await AnalistaAsync(EntornoDeReglas.TitularId)).VersionSeguridad;

        await Entorno.Analistas.ActualizarAsync(
            EntornoDeReglas.TitularId, nombre: null, rol: null, activo: false, SistemasId);

        Assert.Equal(EntornoDeReglas.RespaldoId, (await HiloAsync(uno)).AnalistaAtendiendoId);
        Assert.Equal(EntornoDeReglas.RespaldoId, (await HiloAsync(dos)).AnalistaAtendiendoId);

        var baja = await AnalistaAsync(EntornoDeReglas.TitularId);
        Assert.False(baja.Activo);

        // ARQ-11: su token deja de servir en cuanto sube la versión.
        Assert.Equal(antes + 1, baja.VersionSeguridad);

        Assert.Equal(2, await Entorno.Db.Auditorias.CountAsync(a => a.Accion == "ReasignadaPorBaja"));
    }

    /// <summary>
    /// La cuenta queda sin titular y eso lo tiene que decidir una persona: no se le asigna a nadie
    /// solo, pero tampoco queda escondido (ARQ-09).
    /// </summary>
    [Fact]
    public async Task La_cuenta_queda_sin_titular_y_con_una_alerta()
    {
        await HiloAsignadoAsync("+51900000001");

        await Entorno.Analistas.ActualizarAsync(
            EntornoDeReglas.TitularId, nombre: null, rol: null, activo: false, SistemasId);

        Assert.False(await Entorno.Db.AnalistaCuentas
            .AnyAsync(ac => ac.AnalistaId == EntornoDeReglas.TitularId));

        var alertas = await _arnes.Alertas();
        Assert.Contains(alertas, a => a.Tipo == TiposAlerta.CuentaSinTitular);
    }

    /// <summary>Si el que se va era el respaldo, el hilo vuelve al titular.</summary>
    [Fact]
    public async Task Al_dar_de_baja_al_respaldo_sus_conversaciones_vuelven_al_titular()
    {
        var hilo = await HiloAsignadoAsync("+51900000001");

        var conversacion = await Entorno.Db.Conversaciones.FirstAsync(c => c.ConversacionId == hilo);
        conversacion.AnalistaAtendiendoId = EntornoDeReglas.RespaldoId;
        await Entorno.Db.SaveChangesAsync();

        await Entorno.Analistas.ActualizarAsync(
            EntornoDeReglas.RespaldoId, nombre: null, rol: null, activo: false, SistemasId);

        Assert.Equal(EntornoDeReglas.TitularId, (await HiloAsync(hilo)).AnalistaAtendiendoId);
    }

    /// <summary>
    /// Sin nadie más en la cuenta, el hilo va a «Sin clasificar» y empieza a correr su plazo: es
    /// preferible que lo tome cualquiera a que quede con alguien que ya no está (P3).
    /// </summary>
    [Fact]
    public async Task Sin_reemplazo_el_hilo_queda_sin_clasificar()
    {
        var hilo = await HiloAsignadoAsync("+51900000001");

        await Entorno.Cuentas.QuitarAnalistaAsync(EntornoDeReglas.CuentaId, EntornoDeReglas.RespaldoId);

        await Entorno.Analistas.ActualizarAsync(
            EntornoDeReglas.TitularId, nombre: null, rol: null, activo: false, SistemasId);

        var conversacion = await HiloAsync(hilo);

        Assert.Equal(EstadoConversacion.PendienteClasificar, conversacion.Estado);
        Assert.Null(conversacion.AnalistaAtendiendoId);
        Assert.NotNull(conversacion.FechaPendienteDesde);
    }

    /// <summary>
    /// Una transferencia pendiente de alguien que ya no está deja el hilo en un limbo entre dos
    /// analistas (V21): la que esperaba su respuesta se rechaza y la que él ofreció se retira.
    /// </summary>
    [Fact]
    public async Task Sus_transferencias_pendientes_se_resuelven()
    {
        var hilo = await HiloAsignadoAsync("+51900000001");

        var recibida = await Entorno.Conversaciones.TransferirAsync(
            hilo, EntornoDeReglas.TitularId, EntornoDeReglas.RespaldoId, urgente: false, "Es tuya.");

        await Entorno.Analistas.ActualizarAsync(
            EntornoDeReglas.RespaldoId, nombre: null, rol: null, activo: false, SistemasId);

        var resuelta = await Entorno.Db.Transferencias.AsNoTracking()
            .FirstAsync(t => t.TransferenciaId == recibida.TransferenciaId);

        Assert.Equal(EstadoTransferencia.Rechazada, resuelta.Estado);
        Assert.Equal(EntornoDeReglas.TitularId, (await HiloAsync(hilo)).AnalistaAtendiendoId);
    }

    /// <summary>Jefatura no atiende conversaciones: cambiar de rol reasigna igual que una baja.</summary>
    [Fact]
    public async Task Pasar_a_Jefatura_tambien_reasigna_la_cartera()
    {
        var hilo = await HiloAsignadoAsync("+51900000001");

        await Entorno.Analistas.ActualizarAsync(
            EntornoDeReglas.TitularId, nombre: null, rol: RolAnalista.Jefatura, activo: null, SistemasId);

        var analista = await AnalistaAsync(EntornoDeReglas.TitularId);

        Assert.Equal(RolAnalista.Jefatura, analista.Rol);
        Assert.True(analista.Activo);
        Assert.Equal(EntornoDeReglas.RespaldoId, (await HiloAsync(hilo)).AnalistaAtendiendoId);
    }

    /// <summary>Cambiar el nombre no toca la cartera ni cierra sesiones: no cambia lo que puede hacer.</summary>
    [Fact]
    public async Task Corregir_el_nombre_no_reasigna_nada()
    {
        var hilo = await HiloAsignadoAsync("+51900000001");
        var antes = (await AnalistaAsync(EntornoDeReglas.TitularId)).VersionSeguridad;

        await Entorno.Analistas.ActualizarAsync(
            EntornoDeReglas.TitularId, "Ana María Torres", rol: null, activo: null, SistemasId);

        var analista = await AnalistaAsync(EntornoDeReglas.TitularId);

        Assert.Equal("Ana María Torres", analista.Nombre);
        Assert.Equal(antes, analista.VersionSeguridad);
        Assert.Equal(EntornoDeReglas.TitularId, (await HiloAsync(hilo)).AnalistaAtendiendoId);
    }

    public void Dispose() => _arnes.Dispose();
}
