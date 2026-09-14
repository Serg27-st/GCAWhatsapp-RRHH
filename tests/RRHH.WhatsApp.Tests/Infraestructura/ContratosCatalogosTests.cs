using RRHH.WhatsApp.Contracts.Administracion;
using RRHH.WhatsApp.Domain.Enums;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// El Frontend no ve el dominio (V7), asi que los valores de algunos enums se repiten en Contracts.
/// Estas pruebas son las que avisan si se separan: si no, la pantalla ofreceria un rol o un tipo de
/// campo que la Api rechaza, o no ofreceria uno nuevo.
/// </summary>
public class ContratosCatalogosTests
{
    [Fact]
    public void Los_roles_son_los_del_dominio() =>
        Assert.Equal(Enum.GetNames<RolAnalista>().Order(), Catalogos.Roles.Order());

    [Fact]
    public void Los_tipos_de_campo_son_los_del_dominio() =>
        Assert.Equal(Enum.GetNames<TipoCampoOpcional>().Order(), Catalogos.TiposCampoOpcional.Order());
}
