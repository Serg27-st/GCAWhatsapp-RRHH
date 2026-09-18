using RRHH.WhatsApp.Domain.Entidades;

namespace RRHH.WhatsApp.Tests.Dominio;

/// <summary>
/// El id del boton es lo unico fiable que devuelve WhatsApp (Regla 19). Si un prefijo pisa a otro,
/// el bot interpreta la eleccion del postulante como otra cosa y la conversacion se desvia sin error.
/// </summary>
public class IdsBotonTests
{
    [Fact]
    public void La_pagina_del_menu_viaja_y_se_lee()
    {
        Assert.Equal("pag_2", IdsBoton.ParaPagina(2));
        Assert.Equal(2, IdsBoton.LeerPagina(IdsBoton.ParaPagina(2)));
    }

    [Fact]
    public void El_proceso_elegido_viaja_y_se_lee()
    {
        Assert.Equal("proc_41", IdsBoton.ParaProceso(41));
        Assert.Equal(41, IdsBoton.LeerProceso(IdsBoton.ParaProceso(41)));
    }

    [Fact]
    public void Otra_empresa_se_reconoce_solo_con_su_id_exacto()
    {
        Assert.True(IdsBoton.EsOtraEmpresa(IdsBoton.OtraEmpresa));
        Assert.False(IdsBoton.EsOtraEmpresa("otra_empresa_1"));
        Assert.False(IdsBoton.EsOtraEmpresa(null));
    }

    /// <summary>Cada lector solo reconoce su propio prefijo: un boton de cuenta no es una pagina ni un proceso.</summary>
    [Fact]
    public void Los_prefijos_no_se_confunden_entre_si()
    {
        var botones = new[]
        {
            IdsBoton.ParaCuenta(3), IdsBoton.ParaVacante(3), IdsBoton.ParaPagina(3),
            IdsBoton.ParaProceso(3), IdsBoton.OtraEmpresa
        };

        Assert.Single(botones, b => IdsBoton.LeerCuenta(b) is not null);
        Assert.Single(botones, b => IdsBoton.LeerVacante(b) is not null);
        Assert.Single(botones, b => IdsBoton.LeerPagina(b) is not null);
        Assert.Single(botones, b => IdsBoton.LeerProceso(b) is not null);
        Assert.Single(botones, IdsBoton.EsOtraEmpresa);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("pag_")]
    [InlineData("pag_x")]
    [InlineData("proc_")]
    public void Un_id_incompleto_no_se_lee(string? id)
    {
        Assert.Null(IdsBoton.LeerPagina(id));
        Assert.Null(IdsBoton.LeerProceso(id));
    }
}
