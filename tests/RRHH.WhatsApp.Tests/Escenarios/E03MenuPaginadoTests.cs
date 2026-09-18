using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Tests.Casos;

namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// E03 (FUN-03, AL5): con mas cuentas de las que WhatsApp muestra, el menu se navega por paginas.
/// Antes se truncaba en 10 filas: las cuentas que quedaban fuera eran invisibles para el postulante
/// y la unica senal era una alerta operativa que nadie podia resolver sin cambiar el codigo.
/// </summary>
public class E03MenuPaginadoTests : IDisposable
{
    private readonly ArnesEscenario _arnes = new();

    /// <summary>Lo que ofrece el ultimo menu enviado, como lista de ids de boton.</summary>
    private string[] UltimoMenu() =>
        _arnes.Enviados().Last(e => e.Tipo is "botones" or "lista").Detalle.Split(',');

    /// <summary>Veinte cuentas listas para el menu: activas, con titular y con vacante con formulario.</summary>
    private async Task VeinteCuentasAsync()
    {
        for (var i = 1; i <= 20; i++)
        {
            var cuentaId = 100 + i;

            _arnes.Entorno.Db.Cuentas.Add(new Cuenta { CuentaId = cuentaId, Nombre = $"Cuenta {i:00}", Activo = true });

            _arnes.Entorno.Db.AnalistaCuentas.Add(new AnalistaCuenta
            {
                AnalistaCuentaId = 100 + i,
                AnalistaId = EntornoDeReglas.TitularId,
                CuentaId = cuentaId,
                EsBackup = false
            });

            _arnes.Entorno.Db.Hcs.Add(new Hc
            {
                HcId = 100 + i,
                CuentaId = cuentaId,
                Titulo = $"Vacante {i:00}",
                UrlJobForms = $"https://forms.gle/v{i}",
                Estado = EstadoHc.Abierta,
                FechaCreacion = _arnes.Entorno.Ahora
            });
        }

        // La cuenta sembrada por el entorno tambien cuenta: quedan 21 en total.
        await _arnes.Entorno.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task La_primera_pagina_muestra_nueve_cuentas_y_la_fila_para_ver_mas()
    {
        await VeinteCuentasAsync();

        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Despachar());

        var menu = UltimoMenu();

        Assert.Equal(10, menu.Length);
        Assert.Equal(9, menu.Count(id => id.StartsWith(IdsBoton.PrefijoCuenta)));
        Assert.Equal(IdsBoton.ParaPagina(2), menu[^1]);
    }

    [Fact]
    public async Task La_ultima_pagina_ofrece_volver_al_inicio()
    {
        await VeinteCuentasAsync();

        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Despachar(),
            new Boton(IdsBoton.ParaPagina(3), "Ver mas empresas"),
            new ConsumirOutbox(),
            new Despachar());

        var menu = UltimoMenu();

        // 21 cuentas: 9 en la primera pagina, 9 en la segunda y 3 en la tercera.
        Assert.Equal(3, menu.Count(id => id.StartsWith(IdsBoton.PrefijoCuenta)));
        Assert.Equal(IdsBoton.ParaPagina(1), menu[^1]);
    }

    /// <summary>AL2: navegar el menu no es fallar en elegir, asi que no acerca a «Sin clasificar».</summary>
    [Fact]
    public async Task Navegar_el_menu_no_deriva_a_sin_clasificar()
    {
        await VeinteCuentasAsync();

        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Boton(IdsBoton.ParaPagina(2), "Ver mas empresas"),
            new ConsumirOutbox(),
            new Boton(IdsBoton.ParaPagina(3), "Ver mas empresas"),
            new ConsumirOutbox(),
            new Boton(IdsBoton.ParaPagina(1), "Volver al inicio"),
            new ConsumirOutbox(),
            new Despachar());

        var conversacion = await _arnes.ConversacionAsync();

        Assert.Equal(EstadoConversacion.EnMenuBot, conversacion.Estado);
        Assert.Equal(1, conversacion.IntentosMenuFallidos);
    }

    [Fact]
    public async Task Con_pocas_cuentas_el_menu_no_se_pagina()
    {
        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Despachar());

        var menu = UltimoMenu();

        Assert.DoesNotContain(menu, id => id.StartsWith(IdsBoton.PrefijoPagina));
        Assert.Empty(await _arnes.Alertas());
    }

    public void Dispose() => _arnes.Dispose();
}
