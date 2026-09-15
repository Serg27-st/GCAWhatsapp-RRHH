using RRHH.WhatsApp.Api.Mapeo;
using RRHH.WhatsApp.Domain.Entidades;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// T0.09 (ARQ-01): la bandeja le dice al analista si la ventana de 24 h sigue abierta (Regla 15).
/// Con el reloj del sistema adentro del mapeo, el borde de la ventana no se podía probar sin esperar
/// 24 horas; con el instante como parámetro, la prueba fija el momento.
/// </summary>
public class MapeoBandejaTests
{
    private static readonly DateTime Ahora = new(2026, 9, 14, 15, 0, 0, DateTimeKind.Utc);

    private static Conversacion ConUltimoEntrante(DateTime? fecha) => new()
    {
        TelefonoE164 = "+51987654321",
        FechaUltimoMensajeEntrante = fecha
    };

    [Fact]
    public void Con_el_ultimo_entrante_de_hace_23_horas_la_ventana_sigue_abierta()
    {
        var resumen = ConUltimoEntrante(Ahora.AddHours(-23)).AResumen(Ahora);

        Assert.True(resumen.VentanaAbierta);
    }

    [Fact]
    public void Con_el_ultimo_entrante_de_hace_24_horas_la_ventana_ya_esta_cerrada()
    {
        var resumen = ConUltimoEntrante(Ahora.AddHours(-24)).AResumen(Ahora);

        Assert.False(resumen.VentanaAbierta);
    }

    [Fact]
    public void Sin_entrantes_no_hay_ventana()
    {
        var resumen = ConUltimoEntrante(null).AResumen(Ahora);

        Assert.False(resumen.VentanaAbierta);
    }
}
