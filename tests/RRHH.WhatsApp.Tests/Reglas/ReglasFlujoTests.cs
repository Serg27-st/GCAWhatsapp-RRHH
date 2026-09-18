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

        // COR-13 (M4): quien atendia cuando se decidio escalar. Si entre la decision y la ejecucion
        // cambio de manos, el servicio no escala.
        Assert.Equal(ConstructorContexto.Titular.AnalistaId, escalamiento.AnalistaEsperadoId);

        // El estado lo fija el servicio al escalar: repetirlo aca lo dejaba Escalada aunque el
        // escalamiento se descartara por la revalidacion.
        Assert.Empty(resultado.Acciones.OfType<CambiarEstadoConversacion>());
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

    /// <summary>Viernes 18:00 de Lima (el cierre) y lunes 09:00, la proxima apertura.</summary>
    private static readonly DateTime CierreDelViernes = new(2026, 9, 18, 23, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime AperturaDelLunes = new(2026, 9, 21, 14, 0, 0, DateTimeKind.Utc);

    private static ConstructorContexto FueraDeHorario(DateTime? avisado = null) =>
        new ConstructorContexto()
            .En(new DateTime(2026, 9, 19, 1, 0, 0, DateTimeKind.Utc))
            .ConConversacion(ultimoEntrante: new DateTime(2026, 9, 19, 1, 0, 0, DateTimeKind.Utc), avisoFueraHorario: avisado)
            .FueraDeHorarioDesde(CierreDelViernes, AperturaDelLunes);

    [Fact]
    public async Task Avisa_el_horario_y_cuando_se_retoma_la_atencion()
    {
        var resultado = await _regla.EvaluarAsync(FueraDeHorario().Construir());

        var envio = Assert.Single(resultado.Acciones.OfType<EnviarMensajeBot>());

        Assert.Equal(ClavesPlantilla.FueraDeHorario, envio.ClavePlantilla);
        Assert.Contains("de lunes a viernes de 09:00 a 18:00", envio.Texto);
        Assert.Contains("lunes", envio.Texto);
        Assert.Contains("09:00", envio.Texto);
    }

    /// <summary>COR-03: si el aviso no sale —ventana cerrada y plantilla sin aprobar—, no se sella.</summary>
    [Fact]
    public async Task Sella_el_aviso_solo_si_el_envio_salio()
    {
        var resultado = await _regla.EvaluarAsync(FueraDeHorario().Construir());

        var sello = Assert.Single(resultado.Acciones.OfType<SellarConversacion>());

        Assert.Equal(MarcaConversacion.AvisoFueraHorario, sello.Marca);
        Assert.True(sello.SoloSiSeEnvioAnterior);
    }

    [Fact]
    public void No_repite_el_aviso_dentro_del_mismo_periodo()
    {
        // Avisado despues del cierre del viernes: es el mismo periodo fuera de horario.
        Assert.False(_regla.Aplica(FueraDeHorario(avisado: CierreDelViernes.AddMinutes(30)).Construir()));
    }

    [Fact]
    public void Un_periodo_nuevo_vuelve_a_avisar()
    {
        // El aviso es de la noche anterior, antes del ultimo cierre: otra jornada, otro aviso (A8).
        Assert.True(_regla.Aplica(FueraDeHorario(avisado: CierreDelViernes.AddDays(-1)).Construir()));
    }

    /// <summary>Sin tramos cargados no hay periodo que delimitar, y el aviso diria un horario inventado.</summary>
    [Fact]
    public void Sin_horario_configurado_no_avisa()
    {
        var ctx = new ConstructorContexto().ConConversacion().FueraDeHorario().Construir();

        Assert.False(_regla.Aplica(ctx));
    }

    [Fact]
    public async Task No_detiene_el_flujo_ni_cambia_el_estado()
    {
        // La regla dice que el flujo sigue activo: cualquier analista puede responder igual.
        var resultado = await _regla.EvaluarAsync(FueraDeHorario().Construir());

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

    private static ConstructorContexto EnElMenu(int intentos) =>
        new ConstructorContexto()
            .ConConversacion(estado: EstadoConversacion.EnMenuBot)
            .IntentosMenu(intentos);

    [Fact]
    public async Task Muestra_el_menu_la_primera_vez()
    {
        var resultado = await _regla.EvaluarAsync(EnElMenu(0).Construir());

        var accion = Assert.Single(resultado.Acciones.OfType<MostrarMenuEmpresas>());
        Assert.False(accion.EsReintento);

        // El primer mensaje cuenta como intento pero no como texto no reconocido: el postulante
        // todavia no habia visto ningun menu (A12).
        var intento = Assert.Single(resultado.Acciones.OfType<RegistrarIntentoMenu>());
        Assert.False(intento.TextoNoReconocido);
    }

    [Fact]
    public async Task Reintenta_una_vez_cuando_el_postulante_escribe_texto_libre()
    {
        var resultado = await _regla.EvaluarAsync(EnElMenu(1).Construir());

        var accion = Assert.Single(resultado.Acciones.OfType<MostrarMenuEmpresas>());
        Assert.True(accion.EsReintento);

        // A12: desde este texto que el bot no entendio corre el plazo para derivar por silencio.
        Assert.True(Assert.Single(resultado.Acciones.OfType<RegistrarIntentoMenu>()).TextoNoReconocido);
    }

    [Fact]
    public async Task Deriva_a_la_bandeja_general_tras_agotar_el_reintento()
    {
        var resultado = await _regla.EvaluarAsync(EnElMenu(2).Construir());

        Assert.Single(resultado.Acciones.OfType<DerivarAPendientes>());
        Assert.Empty(resultado.Acciones.OfType<MostrarMenuEmpresas>());
    }

    /// <summary>La cantidad de reintentos es configurable: con dos, el tercer texto todavia ve el menu.</summary>
    [Fact]
    public async Task Los_reintentos_permitidos_salen_de_la_configuracion()
    {
        var ctx = EnElMenu(2).Config(ClavesConfiguracion.MenuReintentosPermitidos, "2").Construir();

        Assert.Single((await _regla.EvaluarAsync(ctx)).Acciones.OfType<MostrarMenuEmpresas>());
    }

    /// <summary>FUN-03: pedir otra pagina no es fallar en elegir, asi que no suma intento.</summary>
    [Fact]
    public async Task Pedir_otra_pagina_no_cuenta_como_intento_fallido()
    {
        var ctx = EnElMenu(1).PidioPagina(2).Construir();

        var resultado = await _regla.EvaluarAsync(ctx);

        var menu = Assert.Single(resultado.Acciones.OfType<MostrarMenuEmpresas>());

        Assert.Equal(2, menu.Pagina);
        Assert.False(menu.EsReintento);
        Assert.Empty(resultado.Acciones.OfType<RegistrarIntentoMenu>());
    }

    /// <summary>B6: en «Sin clasificar» el hilo ya espera a una persona; el bot no vuelve a ofrecer el menu.</summary>
    [Fact]
    public void No_aplica_cuando_el_hilo_ya_esta_en_sin_clasificar()
    {
        var ctx = new ConstructorContexto()
            .ConConversacion(estado: EstadoConversacion.PendienteClasificar)
            .Construir();

        Assert.False(_regla.Aplica(ctx));
    }

    [Fact]
    public void No_aplica_cuando_la_cuenta_ya_fue_identificada()
    {
        var ctx = new ConstructorContexto()
            .ConConversacion(estado: EstadoConversacion.EnMenuBot)
            .ConCuenta(ConstructorContexto.CuentaAlicorp)
            .Construir();

        Assert.False(_regla.Aplica(ctx));
    }
}
