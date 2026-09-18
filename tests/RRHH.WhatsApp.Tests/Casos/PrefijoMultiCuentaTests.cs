using RRHH.WhatsApp.Application.Reglas;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// FUN-09 (A3, R11): el postulante recibe todo por un único hilo. Si está en dos procesos, «te
/// esperamos el lunes a las 9» sin decir de qué empresa es una cita a la que no sabe a dónde ir.
/// </summary>
public class PrefijoMultiCuentaTests
{
    private static readonly DateTime Ahora = new(2026, 9, 14, 15, 0, 0, DateTimeKind.Utc);

    private static PostulacionVigente Proceso(
        int id, int cuentaId, string cuenta, string vacante,
        EstadoPostulacion estado = EstadoPostulacion.EnProceso, int dias = 0) =>
        new(id, cuentaId, cuenta, id * 10, vacante, estado, AnalistaAsignadoId: 10, Ahora.AddDays(-dias));

    private static readonly PostulacionVigente EnAlicorp = Proceso(1, 7, "Alicorp", "Operario");
    private static readonly PostulacionVigente EnIntradevco = Proceso(2, 8, "Intradevco", "Envasador");

    [Fact]
    public void Con_procesos_en_dos_cuentas_el_mensaje_dice_de_cual_habla()
    {
        var texto = PrefijoMultiCuenta.Aplicar("Te esperamos el lunes a las 9.", [EnAlicorp, EnIntradevco], 7);

        Assert.Equal("[Alicorp · Operario] Te esperamos el lunes a las 9.", texto);
    }

    /// <summary>Con una sola cuenta el prefijo sería ruido en cada mensaje: no hay nada que aclarar.</summary>
    [Fact]
    public void Con_un_solo_proceso_el_texto_va_tal_cual()
    {
        Assert.Equal("Hola", PrefijoMultiCuenta.Aplicar("Hola", [EnAlicorp], 7));
    }

    /// <summary>Un proceso cerrado no cuenta: la persona ya no está en esa empresa.</summary>
    [Fact]
    public void Los_procesos_cerrados_no_generan_ambiguedad()
    {
        var descartado = Proceso(2, 8, "Intradevco", "Envasador", EstadoPostulacion.Descartado);

        Assert.Equal("Hola", PrefijoMultiCuenta.Aplicar("Hola", [EnAlicorp, descartado], 7));
    }

    [Fact]
    public void Sin_cuenta_en_contexto_no_hay_prefijo_posible()
    {
        Assert.Equal("Hola", PrefijoMultiCuenta.Aplicar("Hola", [EnAlicorp, EnIntradevco], null));
    }

    /// <summary>Con dos vacantes vivas en la misma cuenta, manda la más reciente: es de la que se habla.</summary>
    [Fact]
    public void Con_dos_vacantes_de_la_misma_cuenta_usa_la_mas_reciente()
    {
        var vieja = Proceso(3, 7, "Alicorp", "Almacenero", dias: 30);

        var texto = PrefijoMultiCuenta.Aplicar("Hola", [vieja, EnAlicorp, EnIntradevco], 7);

        Assert.StartsWith("[Alicorp · Operario]", texto);
    }

    /// <summary>V29: reenviar el mismo texto no lo prefija dos veces.</summary>
    [Fact]
    public void No_se_prefija_dos_veces()
    {
        var unaVez = PrefijoMultiCuenta.Aplicar("Hola", [EnAlicorp, EnIntradevco], 7);

        Assert.Equal(unaVez, PrefijoMultiCuenta.Aplicar(unaVez, [EnAlicorp, EnIntradevco], 7));
    }

    [Fact]
    public void Un_texto_vacio_se_devuelve_igual()
    {
        Assert.Equal(string.Empty, PrefijoMultiCuenta.Aplicar(string.Empty, [EnAlicorp, EnIntradevco], 7));
    }
}
