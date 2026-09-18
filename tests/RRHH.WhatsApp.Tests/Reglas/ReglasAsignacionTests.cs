using RRHH.WhatsApp.Application.Reglas.Implementaciones;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Tests.Reglas;

/// <summary>Regla 1 — asignacion al analista responsable de la cuenta.</summary>
public class R01AsignacionTests
{
    private readonly R01Asignacion _regla = new();

    [Fact]
    public async Task Asigna_al_titular_de_la_cuenta_identificada()
    {
        var ctx = new ConstructorContexto()
            .ConConversacion(estado: EstadoConversacion.PendienteClasificar)
            .ConCuenta(ConstructorContexto.CuentaAlicorp)
            .ConTitular(ConstructorContexto.Titular)
            .Construir();

        var resultado = await _regla.EvaluarAsync(ctx);

        var asignacion = Assert.Single(resultado.Acciones.OfType<AsignarAnalista>());
        Assert.Equal(ConstructorContexto.Titular.AnalistaId, asignacion.AnalistaId);
        Assert.Contains(resultado.Acciones,
            a => a is CambiarEstadoConversacion { Estado: EstadoConversacion.Activa });
    }

    [Fact]
    public async Task No_reasigna_una_conversacion_que_otro_analista_ya_tiene()
    {
        // Evita que un mensaje nuevo le devuelva al titular una conversacion que alguien mas
        // tomo por transferencia (Regla 8) o por escalamiento (Regla 2).
        var ctx = new ConstructorContexto()
            .ConConversacion(analistaAtendiendoId: 99, cuentaContextoId: 1)
            .ConCuenta(ConstructorContexto.CuentaAlicorp)
            .ConTitular(ConstructorContexto.Titular)
            .Construir();

        var resultado = await _regla.EvaluarAsync(ctx);

        Assert.Empty(resultado.Acciones.OfType<AsignarAnalista>());
    }

    [Fact]
    public async Task Avisa_al_analista_si_el_postulante_esta_en_otras_cuentas()
    {
        // Regla 6: el aviso es generico, sin detalle de los mensajes de la otra cuenta.
        var resultado = await _regla.EvaluarAsync(EnOtraCuentaViva().Construir());

        var aviso = Assert.Single(resultado.Acciones.OfType<NotificarAnalista>());
        Assert.Contains("Intradevco", aviso.Mensaje);
    }

    /// <summary>
    /// COR-10 (AL9, P4): el aviso sale cuando la regla asigna. Con el hilo ya asignado, cada mensaje
    /// del postulante repetia el mismo aviso al mismo analista.
    /// </summary>
    [Fact]
    public async Task No_repite_el_aviso_en_cada_mensaje_de_un_hilo_ya_asignado()
    {
        var ctx = EnOtraCuentaViva()
            .ConConversacion(
                analistaAtendiendoId: ConstructorContexto.Titular.AnalistaId,
                cuentaContextoId: ConstructorContexto.CuentaAlicorp.CuentaId)
            .Construir();

        Assert.Empty((await _regla.EvaluarAsync(ctx)).Acciones.OfType<NotificarAnalista>());
    }

    /// <summary>COR-10: al nacer una postulacion, tambien se avisa a los analistas de las otras cuentas.</summary>
    [Fact]
    public async Task Al_completar_el_formulario_se_avisa_a_los_analistas_de_las_dos_cuentas()
    {
        var ctx = EnOtraCuentaViva()
            .Disparador(TipoDisparador.JobFormsCompletado)
            .ConConversacion(
                analistaAtendiendoId: ConstructorContexto.Titular.AnalistaId,
                cuentaContextoId: ConstructorContexto.CuentaAlicorp.CuentaId)
            .Construir();

        var avisos = (await _regla.EvaluarAsync(ctx)).Acciones.OfType<NotificarAnalista>().ToList();

        Assert.Equal(2, avisos.Count);
        Assert.Contains(avisos, a => a.AnalistaId == ConstructorContexto.Titular.AnalistaId);
        Assert.Contains(avisos, a => a.AnalistaId == 77 && a.Mensaje.Contains("Alicorp"));
    }

    [Fact]
    public void Cede_el_paso_a_la_regla_14_cuando_el_titular_esta_ausente()
    {
        var ctx = new ConstructorContexto()
            .ConConversacion()
            .ConCuenta(ConstructorContexto.CuentaAlicorp)
            .ConTitular(ConstructorContexto.Titular)
            .TitularAusente()
            .Construir();

        Assert.False(_regla.Aplica(ctx));
    }

    /// <summary>Un proceso vivo en Intradevco, con su propio analista, y la conversacion en Alicorp.</summary>
    private static ConstructorContexto EnOtraCuentaViva() =>
        new ConstructorContexto()
            .ConConversacion(cuentaContextoId: ConstructorContexto.CuentaAlicorp.CuentaId)
            .ConCuenta(ConstructorContexto.CuentaAlicorp)
            .ConTitular(ConstructorContexto.Titular)
            .ConProcesos(ConstructorContexto.Proceso(
                1, EstadoPostulacion.EnProceso,
                cuentaId: ConstructorContexto.CuentaIntradevco.CuentaId,
                cuenta: ConstructorContexto.CuentaIntradevco.Nombre,
                analistaAsignadoId: 77));
}

/// <summary>Regla 14 — ausencias planificadas.</summary>
public class R14AusenciasTests
{
    private readonly R14Ausencias _regla = new();

    [Fact]
    public async Task Enruta_al_respaldo_sin_esperar_las_2_horas_de_la_regla_2()
    {
        var ctx = new ConstructorContexto()
            .ConConversacion()
            .ConCuenta(ConstructorContexto.CuentaAlicorp)
            .ConTitular(ConstructorContexto.Titular)
            .ConRespaldo(ConstructorContexto.Respaldo)
            .TitularAusente()
            .Construir();

        var resultado = await _regla.EvaluarAsync(ctx);

        var asignacion = Assert.Single(resultado.Acciones.OfType<AsignarAnalista>());
        Assert.Equal(ConstructorContexto.Respaldo.AnalistaId, asignacion.AnalistaId);
    }

    [Fact]
    public async Task Manda_a_la_bandeja_general_si_la_cuenta_no_tiene_respaldo()
    {
        // Sin respaldo configurado la conversacion no puede quedar sin dueno.
        var ctx = new ConstructorContexto()
            .ConConversacion()
            .ConCuenta(ConstructorContexto.CuentaAlicorp)
            .ConTitular(ConstructorContexto.Titular)
            .ConRespaldo(null)
            .TitularAusente()
            .Construir();

        var resultado = await _regla.EvaluarAsync(ctx);

        // FUN-06: derivar sella ademas desde cuando espera, que es lo que le pone plazo al hilo.
        Assert.Single(resultado.Acciones.OfType<DerivarAPendientes>());
        Assert.Empty(resultado.Acciones.OfType<AsignarAnalista>());
    }

    /// <summary>COR-08 (AL4): una transferencia aceptada no se deshace sola con el siguiente mensaje.</summary>
    [Fact]
    public async Task No_le_quita_la_conversacion_a_quien_ya_la_atiende()
    {
        var ctx = new ConstructorContexto()
            .ConConversacion(analistaAtendiendoId: 99, cuentaContextoId: ConstructorContexto.CuentaAlicorp.CuentaId)
            .ConCuenta(ConstructorContexto.CuentaAlicorp)
            .ConTitular(ConstructorContexto.Titular)
            .ConRespaldo(ConstructorContexto.Respaldo)
            .TitularAusente()
            .Construir();

        var resultado = await _regla.EvaluarAsync(ctx);

        Assert.Empty(resultado.Acciones.OfType<AsignarAnalista>());
    }

    /// <summary>Lo que atendia el titular que se fue si pasa al respaldo: es el caso de la regla.</summary>
    [Fact]
    public async Task Lo_que_atendia_el_titular_ausente_pasa_al_respaldo()
    {
        var ctx = new ConstructorContexto()
            .ConConversacion(analistaAtendiendoId: ConstructorContexto.Titular.AnalistaId)
            .ConCuenta(ConstructorContexto.CuentaAlicorp)
            .ConTitular(ConstructorContexto.Titular)
            .ConRespaldo(ConstructorContexto.Respaldo)
            .TitularAusente()
            .Construir();

        var resultado = await _regla.EvaluarAsync(ctx);

        var asignacion = Assert.Single(resultado.Acciones.OfType<AsignarAnalista>());
        Assert.Equal(ConstructorContexto.Respaldo.AnalistaId, asignacion.AnalistaId);
    }

    /// <summary>La cuenta ya estaba fijada: volver a fijarla es una escritura que no cambia nada.</summary>
    [Fact]
    public async Task No_vuelve_a_fijar_la_cuenta_si_ya_es_la_del_contexto()
    {
        var ctx = new ConstructorContexto()
            .ConConversacion(cuentaContextoId: ConstructorContexto.CuentaAlicorp.CuentaId)
            .ConCuenta(ConstructorContexto.CuentaAlicorp)
            .ConTitular(ConstructorContexto.Titular)
            .ConRespaldo(ConstructorContexto.Respaldo)
            .TitularAusente()
            .Construir();

        Assert.Empty((await _regla.EvaluarAsync(ctx)).Acciones.OfType<EstablecerCuentaContexto>());
    }

    [Fact]
    public void No_aplica_cuando_el_titular_esta_disponible()
    {
        var ctx = new ConstructorContexto()
            .ConConversacion()
            .ConCuenta(ConstructorContexto.CuentaAlicorp)
            .ConTitular(ConstructorContexto.Titular)
            .Construir();

        Assert.False(_regla.Aplica(ctx));
    }
}
