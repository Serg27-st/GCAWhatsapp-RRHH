using RRHH.WhatsApp.Application.Reglas.Implementaciones;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Tests.Reglas;

/// <summary>Regla 2 — escalamiento por inactividad, con y sin horario laboral.</summary>
public class R02EscalamientoTests
{
    private readonly R02Escalamiento _regla = new();

    private static ConstructorContexto Base() => new ConstructorContexto()
        .Disparador(TipoDisparador.TiempoTranscurrido)
        .ConConversacion(analistaAtendiendoId: ConstructorContexto.Titular.AnalistaId)
        .ConCuenta(ConstructorContexto.CuentaAlicorp)
        .ConTitular(ConstructorContexto.Titular)
        .ConRespaldo(ConstructorContexto.Respaldo);

    [Fact]
    public async Task Escala_al_respaldo_pasadas_las_2_horas()
    {
        var ctx = Base().SinResponderHace(minutosReloj: 125).Construir();

        var resultado = await _regla.EvaluarAsync(ctx);

        var escalamiento = Assert.Single(resultado.Acciones.OfType<EscalarARespaldo>());
        Assert.Equal(ConstructorContexto.Respaldo.AnalistaId, escalamiento.AnalistaRespaldoId);
        Assert.Contains(resultado.Acciones,
            a => a is CambiarEstadoConversacion { Estado: EstadoConversacion.Escalada });
    }

    [Fact]
    public async Task No_escala_antes_del_plazo()
    {
        var ctx = Base().SinResponderHace(minutosReloj: 90).Construir();

        Assert.Empty((await _regla.EvaluarAsync(ctx)).Acciones);
    }

    [Fact]
    public async Task Con_horario_laboral_activo_no_escala_de_madrugada()
    {
        // El postulante escribio 14 horas de reloj atras, pero solo 30 minutos de esas horas
        // cayeron dentro de la jornada: no corresponde escalar todavia.
        var ctx = Base()
            .Config(ClavesConfiguracion.EscalamientoSoloHorarioLaboral, "true")
            .SinResponderHace(minutosReloj: 840, minutosHabiles: 30)
            .Construir();

        Assert.Empty((await _regla.EvaluarAsync(ctx)).Acciones);
    }

    [Fact]
    public async Task A_reloj_corrido_el_mismo_caso_si_escala()
    {
        var ctx = Base()
            .Config(ClavesConfiguracion.EscalamientoSoloHorarioLaboral, "false")
            .SinResponderHace(minutosReloj: 840, minutosHabiles: 30)
            .Construir();

        Assert.Single((await _regla.EvaluarAsync(ctx)).Acciones.OfType<EscalarARespaldo>());
    }

    [Fact]
    public async Task No_escala_si_el_analista_ya_respondio()
    {
        var ctx = Base().SinResponderHace(minutosReloj: null).Construir();

        Assert.Empty((await _regla.EvaluarAsync(ctx)).Acciones);
    }

    [Fact]
    public void No_vuelve_a_escalar_una_conversacion_que_ya_esta_en_el_respaldo()
    {
        var ctx = new ConstructorContexto()
            .Disparador(TipoDisparador.TiempoTranscurrido)
            .ConConversacion(analistaAtendiendoId: ConstructorContexto.Respaldo.AnalistaId)
            .ConRespaldo(ConstructorContexto.Respaldo)
            .SinResponderHace(600)
            .Construir();

        Assert.False(_regla.Aplica(ctx));
    }
}

/// <summary>Regla 3 — fuera de horario.</summary>
public class R03FueraDeHorarioTests
{
    private readonly R03FueraDeHorario _regla = new();

    [Fact]
    public async Task Envia_la_plantilla_de_horario_en_el_primer_mensaje_fuera_de_jornada()
    {
        var ctx = new ConstructorContexto()
            .ConConversacion(ultimoEntrante: ConstructorContexto.Ahora.AddDays(-2))
            .FueraDeHorario()
            .Construir();

        var resultado = await _regla.EvaluarAsync(ctx);

        var envio = Assert.Single(resultado.Acciones.OfType<EnviarPlantilla>());
        Assert.Equal(ClavesPlantilla.FueraDeHorario, envio.ClavePlantilla);
    }

    [Fact]
    public async Task No_repite_el_aviso_en_cada_mensaje_de_la_misma_tanda()
    {
        // Repetirlo es justo el ruido que eleva los reportes de spam descritos en la Seccion 2.4.
        var ctx = new ConstructorContexto()
            .ConConversacion(ultimoEntrante: ConstructorContexto.Ahora.AddMinutes(-10))
            .FueraDeHorario()
            .Construir();

        Assert.Empty((await _regla.EvaluarAsync(ctx)).Acciones);
    }

    [Fact]
    public async Task No_detiene_el_flujo_ni_cambia_el_estado()
    {
        // La regla dice que el flujo sigue activo: cualquier analista puede responder igual.
        var ctx = new ConstructorContexto()
            .ConConversacion(ultimoEntrante: ConstructorContexto.Ahora.AddDays(-2))
            .FueraDeHorario()
            .Construir();

        var resultado = await _regla.EvaluarAsync(ctx);

        Assert.False(resultado.DetenerEvaluacion);
        Assert.Empty(resultado.Acciones.OfType<CambiarEstadoConversacion>());
    }

    [Fact]
    public void No_aplica_dentro_del_horario()
    {
        var ctx = new ConstructorContexto().ConConversacion().Construir();

        Assert.False(_regla.Aplica(ctx));
    }
}

/// <summary>Regla 19 — fallback de menu no reconocido.</summary>
public class R19FallbackMenuTests
{
    private readonly R19FallbackMenu _regla = new();

    [Fact]
    public async Task Muestra_el_menu_la_primera_vez()
    {
        var ctx = new ConstructorContexto().ConConversacion().IntentosMenu(0).Construir();

        var accion = Assert.Single((await _regla.EvaluarAsync(ctx)).Acciones.OfType<MostrarMenuEmpresas>());
        Assert.False(accion.EsReintento);
    }

    [Fact]
    public async Task Reintenta_una_vez_cuando_el_postulante_escribe_texto_libre()
    {
        var ctx = new ConstructorContexto().ConConversacion().IntentosMenu(1).Construir();

        var accion = Assert.Single((await _regla.EvaluarAsync(ctx)).Acciones.OfType<MostrarMenuEmpresas>());
        Assert.True(accion.EsReintento);
    }

    [Fact]
    public async Task Deriva_a_la_bandeja_general_tras_agotar_el_reintento()
    {
        var ctx = new ConstructorContexto().ConConversacion().IntentosMenu(2).Construir();

        var resultado = await _regla.EvaluarAsync(ctx);

        Assert.Contains(resultado.Acciones,
            a => a is CambiarEstadoConversacion { Estado: EstadoConversacion.PendienteClasificar });
        Assert.Empty(resultado.Acciones.OfType<MostrarMenuEmpresas>());
    }

    [Fact]
    public void No_aplica_cuando_la_cuenta_ya_fue_identificada()
    {
        var ctx = new ConstructorContexto()
            .ConConversacion()
            .ConCuenta(ConstructorContexto.CuentaAlicorp)
            .Construir();

        Assert.False(_regla.Aplica(ctx));
    }
}
