using Microsoft.Extensions.Logging.Abstractions;
using RRHH.WhatsApp.Application.Reglas;
using RRHH.WhatsApp.Application.Reglas.Implementaciones;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Tests.Reglas;

public class MotorReglasTests
{
    private static MotorReglas Motor(params IReglaNegocio[] reglas) =>
        new(reglas, NullLogger<MotorReglas>.Instance);

    [Fact]
    public async Task Evalua_las_reglas_en_orden_de_prioridad()
    {
        var orden = new List<string>();
        var motor = Motor(
            new ReglaEspia("B", prioridad: 50, orden),
            new ReglaEspia("A", prioridad: 10, orden),
            new ReglaEspia("C", prioridad: 30, orden));

        await motor.ProcesarAsync(new ConstructorContexto().ConConversacion().Construir());

        Assert.Equal(["A", "C", "B"], orden);
    }

    [Fact]
    public async Task Una_regla_que_detiene_impide_que_corran_las_siguientes()
    {
        // Es el comportamiento del que depende la Regla 15: si bloquea el envio, ninguna regla
        // posterior debe poder producir un mensaje.
        var orden = new List<string>();
        var motor = Motor(
            new ReglaEspia("primera", prioridad: 10, orden, detiene: true),
            new ReglaEspia("segunda", prioridad: 20, orden));

        await motor.ProcesarAsync(new ConstructorContexto().ConConversacion().Construir());

        Assert.Equal(["primera"], orden);
    }

    [Fact]
    public async Task Una_regla_que_revienta_no_tumba_el_procesamiento_completo()
    {
        var orden = new List<string>();
        var motor = Motor(
            new ReglaQueFalla(prioridad: 10),
            new ReglaEspia("sobreviviente", prioridad: 20, orden));

        var acciones = await motor.ProcesarAsync(new ConstructorContexto().ConConversacion().Construir());

        Assert.Equal(["sobreviviente"], orden);
        Assert.Single(acciones);
    }

    [Fact]
    public async Task Un_envio_sin_optin_no_deja_pasar_ninguna_otra_accion()
    {
        // Prueba de integracion entre reglas reales: el mensaje fuera de horario no debe salir
        // si la Regla 15 ya bloqueo el envio.
        var motor = Motor(new R15OptInYVentana(), new R03FueraDeHorario());

        var ctx = new ConstructorContexto()
            .Disparador(TipoDisparador.EnvioSaliente)
            .ConConversacion(optIn: null)
            .FueraDeHorario()
            .Construir();

        var acciones = await motor.ProcesarAsync(ctx);

        Assert.Single(acciones);
        Assert.IsType<BloquearEnvio>(acciones[0]);
    }

    [Fact]
    public async Task Un_mensaje_entrante_sin_cuenta_identificada_solo_muestra_el_menu()
    {
        // La Regla 19 corre pero la Regla 1 no tiene cuenta a la que asignar.
        var motor = Motor(new R19FallbackMenu(), new R01Asignacion());

        var ctx = new ConstructorContexto()
            .ConConversacion(estado: EstadoConversacion.PendienteClasificar)
            .IntentosMenu(0)
            .Construir();

        var acciones = await motor.ProcesarAsync(ctx);

        Assert.Single(acciones.OfType<MostrarMenuEmpresas>());
        Assert.Empty(acciones.OfType<AsignarAnalista>());
    }

    private sealed class ReglaEspia(string nombre, int prioridad, List<string> orden, bool detiene = false)
        : IReglaNegocio
    {
        public string Codigo => nombre;
        public string Descripcion => "Regla de prueba.";
        public int Prioridad => prioridad;
        public bool Aplica(ContextoRegla contexto) => true;

        public Task<ResultadoRegla> EvaluarAsync(ContextoRegla contexto, CancellationToken ct = default)
        {
            orden.Add(nombre);
            var accion = new RegistrarAuditoria(nombre, "espia");
            return Task.FromResult(detiene ? ResultadoRegla.Detener(accion) : ResultadoRegla.Con(accion));
        }
    }

    private sealed class ReglaQueFalla(int prioridad) : IReglaNegocio
    {
        public string Codigo => "explota";
        public string Descripcion => "Regla que lanza excepcion.";
        public int Prioridad => prioridad;
        public bool Aplica(ContextoRegla contexto) => true;

        public Task<ResultadoRegla> EvaluarAsync(ContextoRegla contexto, CancellationToken ct = default) =>
            throw new InvalidOperationException("fallo simulado");
    }
}
