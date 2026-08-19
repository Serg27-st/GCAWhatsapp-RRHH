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
        var ctx = new ConstructorContexto()
            .ConConversacion(cuentaContextoId: 1)
            .ConCuenta(ConstructorContexto.CuentaAlicorp)
            .ConTitular(ConstructorContexto.Titular)
            .EnOtrasCuentas(ConstructorContexto.CuentaIntradevco)
            .Construir();

        var resultado = await _regla.EvaluarAsync(ctx);

        var aviso = Assert.Single(resultado.Acciones.OfType<NotificarAnalista>());
        Assert.Contains("Intradevco", aviso.Mensaje);
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

        Assert.Contains(resultado.Acciones,
            a => a is CambiarEstadoConversacion { Estado: EstadoConversacion.PendienteClasificar });
        Assert.Empty(resultado.Acciones.OfType<AsignarAnalista>());
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
