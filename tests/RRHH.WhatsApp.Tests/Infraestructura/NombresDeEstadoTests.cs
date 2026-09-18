using System.Reflection;
using RRHH.WhatsApp.Contracts.Bandeja;
using RRHH.WhatsApp.Domain.Enums;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// El Frontend no conoce los enums de Domain: compara el estado que le llega como texto contra las
/// constantes de Contracts. Si alguien renombra un valor del enum, la pantalla deja de reconocerlo sin
/// que falle nada; esto es lo que lo hace fallar.
/// </summary>
public class NombresDeEstadoTests
{
    public static TheoryData<Type, Type> Pares => new()
    {
        { typeof(EstadosConversacion), typeof(EstadoConversacion) },
        { typeof(EstadosPostulacion), typeof(EstadoPostulacion) },
        { typeof(EstadosEntrega), typeof(EstadoEntrega) },
        { typeof(EstadosAdjunto), typeof(EstadoAdjunto) }
    };

    [Theory]
    [MemberData(nameof(Pares))]
    public void Cada_constante_nombra_un_valor_del_enum(Type constantes, Type enumeracion)
    {
        var nombres = Enum.GetNames(enumeracion);

        var valores = constantes
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(valores);
        Assert.All(valores, v => Assert.Contains(v, nombres));
    }
}
