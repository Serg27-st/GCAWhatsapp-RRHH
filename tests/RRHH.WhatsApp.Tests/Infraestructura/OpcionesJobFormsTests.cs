using Microsoft.Extensions.Configuration;
using RRHH.WhatsApp.Api.Configuracion;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// COR-15/M8: <c>DominiosCvPermitidos</c> valida el enlace del CV que llega del JobForms.
/// <para>
/// El binder de configuracion de .NET, si la propiedad ya trae un array con datos por defecto,
/// AGREGA los indices que encuentra en la configuracion en vez de reemplazarlos — asi que sembrar
/// el default directo en la propiedad duplicaria (o mezclaria) los dominios permitidos apenas
/// alguien cargara <c>appsettings.json</c>. Esta prueba deja esa trampa explicita.
/// </para>
/// </summary>
public class OpcionesJobFormsTests
{
    private static OpcionesJobForms Bindear(params string[] dominios)
    {
        var datos = new Dictionary<string, string?>();

        for (var i = 0; i < dominios.Length; i++)
            datos[$"JobForms:DominiosCvPermitidos:{i}"] = dominios[i];

        var configuracion = new ConfigurationBuilder().AddInMemoryCollection(datos).Build();

        var opciones = new OpcionesJobForms();
        configuracion.GetSection(OpcionesJobForms.Seccion).Bind(opciones);

        return opciones;
    }

    [Fact]
    public void Sin_configuracion_el_efectivo_es_el_default()
    {
        var opciones = Bindear();

        Assert.Equal(
            OpcionesJobForms.DominiosCvPermitidosPorDefecto, opciones.DominiosCvPermitidosEfectivos);
    }

    [Fact]
    public void Con_configuracion_el_efectivo_es_exactamente_lo_configurado_sin_sumar_el_default()
    {
        var opciones = Bindear("misempresa.sharepoint.com");

        Assert.Equal(["misempresa.sharepoint.com"], opciones.DominiosCvPermitidosEfectivos);
    }

    [Fact]
    public void Configurar_los_mismos_dos_dominios_del_default_no_los_duplica()
    {
        var opciones = Bindear("drive.google.com", "docs.google.com");

        Assert.Equal(["drive.google.com", "docs.google.com"], opciones.DominiosCvPermitidosEfectivos);
    }
}
