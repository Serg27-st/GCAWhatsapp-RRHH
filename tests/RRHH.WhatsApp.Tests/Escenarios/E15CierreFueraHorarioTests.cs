using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Tests.Casos;
using RRHH.WhatsApp.Domain.Entidades;

namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// E15 (FUN-10, A11): el cierre de cortesía sale una sola vez y dentro del horario de atención.
/// Despedir a alguien a las once de la noche es peor que hacerlo al día siguiente, y el descarte
/// suele decidirse cuando el analista cierra su jornada.
/// </summary>
public class E15CierreFueraHorarioTests : IDisposable
{
    private readonly ArnesEscenario _arnes = new();

    /// <summary>El cierre, salga como texto o como plantilla aprobada (COR-03).</summary>
    private int Cierres() => _arnes.Enviados().Count(e =>
        e.Detalle.Contains("Agradecemos tu interes") || e.Detalle == ClavesPlantilla.CierreCortesia);

    /// <summary>Lo que hace una persona en la administracion cuando Meta aprueba: nunca el codigo.</summary>
    private async Task AprobarCierreAsync()
    {
        var plantilla = await _arnes.Entorno.Db.Plantillas
            .FirstAsync(p => p.Clave == ClavesPlantilla.CierreCortesia);

        plantilla.Activa = true;

        await _arnes.Entorno.Db.SaveChangesAsync();
    }

    private Task<Domain.Entidades.Postulacion> PostulacionAsync() =>
        _arnes.Entorno.Db.Postulaciones.AsNoTracking().FirstAsync();

    /// <summary>Deja una postulación creada por el circuito real, con horario comercial cargado.</summary>
    private async Task<int> ConPostulacionAsync()
    {
        await _arnes.ConHorarioComercialAsync();

        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Boton("cuenta_7", "Alicorp"),
            new ConsumirOutbox(),
            new Despachar(),
            new Formulario(),
            new ConsumirOutbox(),
            new Despachar());

        return (await PostulacionAsync()).PostulacionId;
    }

    private async Task DescartarAsync(int postulacionId, bool enviarCierre = true)
    {
        var descartado = await _arnes.Entorno.Db.EtapasKanban.AsNoTracking()
            .FirstAsync(e => e.EstadoResultante == EstadoPostulacion.Descartado);

        await _arnes.Entorno.Bandeja.MoverEtapaAsync(
            postulacionId, descartado.EtapaId, EntornoDeReglas.TitularId, enviarCierre);

        await _arnes.ConversarAsync(new ConsumirOutbox(), new Despachar());
    }

    [Fact]
    public async Task Descartar_en_horario_manda_el_cierre_una_sola_vez()
    {
        var postulacionId = await ConPostulacionAsync();

        await DescartarAsync(postulacionId);

        Assert.Equal(1, Cierres());

        var postulacion = await PostulacionAsync();

        Assert.NotNull(postulacion.FechaCierreCortesia);
        Assert.False(postulacion.CierreCortesiaPendiente);

        // Otra vuelta del barrido no lo repite: el sello es lo que lo impide (P4).
        await _arnes.ConversarAsync(new Barrido(), new Despachar());

        Assert.Equal(1, Cierres());
    }

    /// <summary>A11: el analista puede decir que no en el diálogo de descarte.</summary>
    [Fact]
    public async Task Si_el_analista_no_lo_pide_no_sale_ningun_cierre()
    {
        var postulacionId = await ConPostulacionAsync();

        await DescartarAsync(postulacionId, enviarCierre: false);

        Assert.Equal(0, Cierres());
        Assert.False((await PostulacionAsync()).CierreCortesiaPendiente);
    }

    /// <summary>Fuera de hora queda pedido, y el barrido lo manda cuando la jornada abre.</summary>
    [Fact]
    public async Task Descartar_fuera_de_horario_lo_deja_pendiente_hasta_la_proxima_jornada()
    {
        var postulacionId = await ConPostulacionAsync();

        // Para el lunes la ventana de 24h ya cerró, así que el cierre necesita su plantilla aprobada
        // (Regla 15). Es el caso real: el descarte se decide días después del último mensaje.
        await AprobarCierreAsync();

        // Viernes 20:00 de Lima: la jornada ya cerró.
        await _arnes.ConversarAsync(new Avanzar(TimeSpan.FromDays(4) + TimeSpan.FromHours(10)));

        await DescartarAsync(postulacionId);

        Assert.Equal(0, Cierres());
        Assert.True((await PostulacionAsync()).CierreCortesiaPendiente);

        // Lunes 10:00 de Lima (del viernes 20:00, dos días y catorce horas): el barrido lo toma y
        // recién ahí sale.
        await _arnes.ConversarAsync(
            new Avanzar(TimeSpan.FromDays(2) + TimeSpan.FromHours(14)),
            new Barrido(),
            new Despachar());

        Assert.Equal(1, Cierres());
        Assert.NotNull((await PostulacionAsync()).FechaCierreCortesia);
    }

    public void Dispose() => _arnes.Dispose();
}
