using System.Diagnostics;
using RRHH.WhatsApp.Infrastructure.Proveedores;

namespace RRHH.WhatsApp.Tests.Proveedores;

/// <summary>
/// Es la guarda de la Seccion 9.6.4 (COR-12/AL8): el volumen saliente sin control es una de las
/// causas del bloqueo original. Movida a su propio archivo al separarse del constructor de tope
/// fijo: <see cref="LimitadorEnvio"/> ahora tambien acepta una funcion asincrona que consulta
/// <c>envio.maximo_por_segundo</c> en cada turno (ver <see cref="ProveedorParametrosEnvioTests"/>
/// para la integracion completa con la cache de 30 s).
/// </summary>
public class LimitadorEnvioTests
{
    [Fact]
    public async Task Deja_pasar_los_primeros_envios_sin_esperar()
    {
        using var limitador = new LimitadorEnvio(maximoPorSegundo: 5);
        var reloj = Stopwatch.StartNew();

        for (var i = 0; i < 5; i++)
            await limitador.EsperarTurnoAsync();

        Assert.True(reloj.ElapsedMilliseconds < 200, $"No deberia haber esperado, pero tardo {reloj.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public async Task Frena_cuando_se_supera_el_tope_por_segundo()
    {
        using var limitador = new LimitadorEnvio(maximoPorSegundo: 2);
        var reloj = Stopwatch.StartNew();

        for (var i = 0; i < 3; i++)
            await limitador.EsperarTurnoAsync();

        Assert.True(reloj.ElapsedMilliseconds >= 900,
            $"El tercer envio debio esperar cerca de un segundo, pero paso en {reloj.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public async Task Un_tope_fijo_invalido_no_deja_el_limitador_abierto()
    {
        // Antes esto se verificaba leyendo limitador.MaximoPorSegundo == 1; esa propiedad
        // desaparecio porque con el constructor de funcion no hay un "tope actual" fijo que
        // exponer sin invocar la funcion (que puede tocar la base). Se verifica por
        // comportamiento: con tope 0 -> 1, el segundo envio del mismo instante tiene que esperar.
        using var limitador = new LimitadorEnvio(maximoPorSegundo: 0);
        var reloj = Stopwatch.StartNew();

        await limitador.EsperarTurnoAsync();
        await limitador.EsperarTurnoAsync();

        Assert.True(reloj.ElapsedMilliseconds >= 900,
            $"El segundo envio debio esperar cerca de un segundo (tope efectivo 1), pero paso en {reloj.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public async Task Un_maximoActual_no_positivo_tambien_se_trata_como_uno()
    {
        using var limitador = new LimitadorEnvio(_ => ValueTask.FromResult(0));
        var reloj = Stopwatch.StartNew();

        await limitador.EsperarTurnoAsync();
        await limitador.EsperarTurnoAsync();

        Assert.True(reloj.ElapsedMilliseconds >= 900,
            $"El segundo envio debio esperar cerca de un segundo (tope efectivo 1), pero paso en {reloj.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public async Task Bajar_el_tope_con_la_ventana_llena_espera_en_vez_de_romperse()
    {
        // Desvio intencional de 03 §COR-12, documentado en LimitadorEnvio.cs: si el tope baja
        // mientras hay mas envios en la ventana que el nuevo maximo, el limitador no debe
        // lanzar ni quedar en espera ocupada, solo esperar a que la ventana se vacie por debajo
        // del tope vigente.
        var maximo = 5;
        using var limitador = new LimitadorEnvio(_ => ValueTask.FromResult(maximo));

        for (var i = 0; i < 5; i++)
            await limitador.EsperarTurnoAsync();

        maximo = 2; // La ventana ya tiene 5 envios; el nuevo tope es menor que eso.

        var reloj = Stopwatch.StartNew();
        await limitador.EsperarTurnoAsync();
        reloj.Stop();

        Assert.True(reloj.ElapsedMilliseconds >= 900,
            $"Con la ventana llena por encima del nuevo tope, el envio siguiente debio esperar cerca de un segundo, pero paso en {reloj.ElapsedMilliseconds} ms.");
    }
}
