using RRHH.WhatsApp.Api.Mapeo;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;

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

    /// <summary>
    /// FUN-05 (A9): «vencida» es lo que la bandeja muestra en rojo. Se calcula igual que el aviso a
    /// Jefatura —sellado y con el postulante todavia esperando— para que la pantalla y la regla no
    /// digan cosas distintas del mismo hilo.
    /// </summary>
    [Fact]
    public void Una_conversacion_con_aviso_a_Jefatura_y_sin_responder_esta_vencida()
    {
        var conversacion = ConUltimoEntrante(Ahora.AddHours(-5));
        conversacion.Estado = EstadoConversacion.Escalada;
        conversacion.FechaEscalamiento = Ahora.AddHours(-4);
        conversacion.FechaAvisoSegundoNivel = Ahora.AddHours(-1);

        Assert.True(conversacion.AResumen(Ahora).Vencida);
    }

    [Fact]
    public void Sin_el_aviso_a_Jefatura_no_esta_vencida()
    {
        var conversacion = ConUltimoEntrante(Ahora.AddHours(-5));
        conversacion.Estado = EstadoConversacion.Escalada;
        conversacion.FechaEscalamiento = Ahora.AddHours(-4);

        Assert.False(conversacion.AResumen(Ahora).Vencida);
    }

    /// <summary>Respondida deja de estar vencida aunque el aviso siga sellado: ya no espera a nadie.</summary>
    [Fact]
    public void Respondida_deja_de_estar_vencida()
    {
        var conversacion = ConUltimoEntrante(Ahora.AddHours(-5));
        conversacion.Estado = EstadoConversacion.Escalada;
        conversacion.FechaEscalamiento = Ahora.AddHours(-4);
        conversacion.FechaAvisoSegundoNivel = Ahora.AddHours(-1);
        conversacion.FechaUltimaRespuestaAnalista = Ahora.AddMinutes(-10);

        Assert.False(conversacion.AResumen(Ahora).Vencida);
    }

    private static Mensaje FallidoDelAnalista(ClaseFallo clase, int? plantillaId = null) => new()
    {
        ConversacionId = 1,
        Direccion = DireccionMensaje.Saliente,
        Contenido = "Te esperamos el lunes a las 9.",
        AnalistaId = 10,
        PlantillaId = plantillaId,
        EstadoEntrega = EstadoEntrega.Fallido,
        ClaseFallo = clase,
        ErrorProveedor = "131026 Message undeliverable",
        FechaEnvio = Ahora
    };

    /// <summary>FUN-13: rechazado en firme, no salió; reenviarlo no lo duplica.</summary>
    [Fact]
    public void Un_texto_del_analista_rechazado_en_firme_se_puede_reintentar()
    {
        var resumen = FallidoDelAnalista(ClaseFallo.Permanente).AResumen();

        Assert.True(resumen.Reintentable);
        Assert.Equal("131026 Message undeliverable", resumen.Error);
    }

    /// <summary>
    /// Un fallo ambiguo pudo haber salido: reenviarlo arriesga el duplicado que bloqueó la línea. Uno
    /// transitorio ya lo reintenta el Worker, y ofrecerlo a mano lo mandaría dos veces.
    /// </summary>
    [Theory]
    [InlineData(ClaseFallo.Ambiguo)]
    [InlineData(ClaseFallo.Transitorio)]
    public void Un_fallo_ambiguo_o_transitorio_no_se_ofrece_para_reintentar(ClaseFallo clase)
    {
        var resumen = FallidoDelAnalista(clase).AResumen();

        Assert.False(resumen.Reintentable);
        Assert.NotNull(resumen.Error);
    }

    /// <summary>Una plantilla se vuelve a elegir con sus parámetros: «Reintentar» repite solo texto libre.</summary>
    [Fact]
    public void Una_plantilla_fallida_no_se_ofrece_para_reintentar()
    {
        Assert.False(FallidoDelAnalista(ClaseFallo.Permanente, plantillaId: 1).AResumen().Reintentable);
    }

    /// <summary>El motivo de un fallo viejo no se muestra si el reintento salió bien.</summary>
    [Fact]
    public void Sin_fallo_no_hay_error_que_mostrar()
    {
        var mensaje = FallidoDelAnalista(ClaseFallo.Ninguno);
        mensaje.EstadoEntrega = EstadoEntrega.Entregado;

        var resumen = mensaje.AResumen();

        Assert.Null(resumen.Error);
        Assert.False(resumen.Reintentable);
    }
}
