using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RRHH.WhatsApp.Infrastructure.Proveedores;

namespace RRHH.WhatsApp.Tests.Proveedores;

/// <summary>
/// Validacion de la firma del webhook. Es la puerta de entrada publica del sistema: si acepta un
/// payload que no vino de 360dialog, cualquiera puede inyectar conversaciones falsas.
/// </summary>
public class FirmaWebhookTests
{
    private const string Secreto = "secreto-de-plataforma-de-prueba";

    private static Dialog360Provider Proveedor(string? secreto = Secreto) =>
        new(new HttpClient { BaseAddress = new Uri("https://ejemplo.invalid/") },
            Options.Create(new Dialog360Opciones { ApiKey = "clave", SecretoWebhook = secreto ?? string.Empty }),
            new LimitadorEnvio(10),
            NullLogger<Dialog360Provider>.Instance);

    private static Dictionary<string, string> ConFirma(string firma) =>
        new() { [Dialog360Provider.CabeceraFirma] = firma };

    [Fact]
    public void Acepta_una_firma_correcta()
    {
        var cuerpo = PayloadsDePrueba.MensajeDeTexto;
        var firma = Dialog360Provider.CalcularFirma(cuerpo, Secreto);

        Assert.True(Proveedor().ValidarFirma(cuerpo, ConFirma(firma)));
    }

    [Fact]
    public void Rechaza_una_firma_calculada_con_otro_secreto()
    {
        var cuerpo = PayloadsDePrueba.MensajeDeTexto;
        var firma = Dialog360Provider.CalcularFirma(cuerpo, "otro-secreto");

        Assert.False(Proveedor().ValidarFirma(cuerpo, ConFirma(firma)));
    }

    [Fact]
    public void Rechaza_si_el_cuerpo_fue_alterado()
    {
        // El escenario real: alguien intercepta y cambia el numero de telefono del mensaje.
        var original = PayloadsDePrueba.MensajeDeTexto;
        var firma = Dialog360Provider.CalcularFirma(original, Secreto);
        var alterado = original.Replace("51987654321", "51900000000");

        Assert.False(Proveedor().ValidarFirma(alterado, ConFirma(firma)));
    }

    [Fact]
    public void Rechaza_cuando_falta_la_cabecera()
    {
        Assert.False(Proveedor().ValidarFirma(PayloadsDePrueba.MensajeDeTexto, new Dictionary<string, string>()));
    }

    [Fact]
    public void Falla_cerrado_si_no_hay_secreto_configurado()
    {
        // Sin secreto no se puede verificar nada, asi que no se acepta nada. Preferimos no recibir
        // a procesar un payload que no podemos atribuir.
        var cuerpo = PayloadsDePrueba.MensajeDeTexto;
        var firma = Dialog360Provider.CalcularFirma(cuerpo, Secreto);

        Assert.False(Proveedor(secreto: null).ValidarFirma(cuerpo, ConFirma(firma)));
    }

    [Fact]
    public void Acepta_la_cabecera_con_el_prefijo_del_algoritmo()
    {
        // Algunas pasarelas la envian al estilo de Meta: "sha256=<hex>".
        var cuerpo = PayloadsDePrueba.MensajeDeTexto;
        var firma = Dialog360Provider.CalcularFirma(cuerpo, Secreto);

        Assert.True(Proveedor().ValidarFirma(cuerpo, ConFirma("sha256=" + firma)));
    }

    [Fact]
    public void El_nombre_de_la_cabecera_no_distingue_mayusculas()
    {
        var cuerpo = PayloadsDePrueba.MensajeDeTexto;
        var firma = Dialog360Provider.CalcularFirma(cuerpo, Secreto);

        var cabeceras = new Dictionary<string, string> { ["X-360Dialog-Signature"] = firma };

        Assert.True(Proveedor().ValidarFirma(cuerpo, cabeceras));
    }

    [Fact]
    public void La_firma_es_estable_para_el_mismo_cuerpo_y_secreto()
    {
        var a = Dialog360Provider.CalcularFirma("hola", Secreto);
        var b = Dialog360Provider.CalcularFirma("hola", Secreto);

        Assert.Equal(a, b);
        Assert.Equal(64, a.Length); // HMAC-SHA256 en hexadecimal.
    }
}
