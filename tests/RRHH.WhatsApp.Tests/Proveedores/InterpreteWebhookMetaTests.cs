using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Infrastructure.Proveedores;

namespace RRHH.WhatsApp.Tests.Proveedores;

public class InterpreteWebhookMetaTests
{
    [Fact]
    public void Interpreta_un_mensaje_de_texto_con_su_nombre_de_perfil()
    {
        var mensaje = Assert.Single(InterpreteWebhookMeta.Mensajes(PayloadsDePrueba.MensajeDeTexto, DateTime.UtcNow));

        Assert.Equal("wamid.HBgLNTE5ODc2NTQzMjEVAgASGBQ", mensaje.ProviderMessageId);
        Assert.Equal("+51987654321", mensaje.TelefonoE164);
        Assert.Equal("Maria Quispe", mensaje.NombrePerfil);
        Assert.Equal("Hola, vi el aviso de trabajo", mensaje.Contenido);
        Assert.Null(mensaje.IdBotonPulsado);
    }

    [Fact]
    public void Normaliza_el_telefono_a_E164()
    {
        // Meta entrega el numero sin el signo; el dominio lo guarda con el.
        Assert.Equal("+51987654321", InterpreteWebhookMeta.NormalizarTelefono("51987654321"));
        Assert.Equal("+51987654321", InterpreteWebhookMeta.NormalizarTelefono("+51 987 654 321"));
    }

    [Fact]
    public void Convierte_el_timestamp_unix_a_UTC()
    {
        var mensaje = Assert.Single(InterpreteWebhookMeta.Mensajes(PayloadsDePrueba.MensajeDeTexto, DateTime.UtcNow));

        Assert.Equal(DateTimeKind.Utc, mensaje.FechaUtc.Kind);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1755600000).UtcDateTime, mensaje.FechaUtc);
    }

    [Fact]
    public void Extrae_el_id_del_boton_pulsado()
    {
        // Es el dato del que depende la Regla 19 para saber si eligio una opcion valida.
        var mensaje = Assert.Single(InterpreteWebhookMeta.Mensajes(PayloadsDePrueba.RespuestaDeBoton, DateTime.UtcNow));

        Assert.Equal("cuenta_7", mensaje.IdBotonPulsado);
        Assert.Equal("Alicorp", mensaje.Contenido);
    }

    [Fact]
    public void Extrae_el_id_de_una_opcion_de_lista()
    {
        var mensaje = Assert.Single(InterpreteWebhookMeta.Mensajes(PayloadsDePrueba.RespuestaDeLista, DateTime.UtcNow));

        Assert.Equal("hc_42", mensaje.IdBotonPulsado);
        Assert.Equal("Operario de produccion", mensaje.Contenido);
    }

    [Fact]
    public void Extrae_el_payload_de_una_respuesta_rapida_de_plantilla()
    {
        // En este tipo el identificador viaja en payload y no en id.
        var mensaje = Assert.Single(InterpreteWebhookMeta.Mensajes(PayloadsDePrueba.BotonDePlantilla, DateTime.UtcNow));

        Assert.Equal("confirmar_entrevista", mensaje.IdBotonPulsado);
        Assert.Equal("Confirmo", mensaje.Contenido);
    }

    [Fact]
    public void Un_adjunto_deja_constancia_del_tipo_en_vez_de_perderse()
    {
        var mensaje = Assert.Single(InterpreteWebhookMeta.Mensajes(PayloadsDePrueba.Adjunto, DateTime.UtcNow));

        Assert.Equal("[document]", mensaje.Contenido);
        Assert.Null(mensaje.IdBotonPulsado);
    }

    [Fact]
    public void Interpreta_varios_mensajes_en_un_mismo_evento()
    {
        var mensajes = InterpreteWebhookMeta.Mensajes(PayloadsDePrueba.DosMensajes, DateTime.UtcNow);

        Assert.Equal(2, mensajes.Count);
        Assert.Equal(["wamid.A", "wamid.B"], mensajes.Select(m => m.ProviderMessageId));
    }

    [Fact]
    public void Los_acuses_de_entrega_van_por_separado_de_los_mensajes()
    {
        // No abren la ventana de 24h ni cuentan como opt-in, por eso no se mezclan.
        Assert.Empty(InterpreteWebhookMeta.Mensajes(PayloadsDePrueba.AcusesDeEntrega, DateTime.UtcNow));

        var acuses = InterpreteWebhookMeta.Estados(PayloadsDePrueba.AcusesDeEntrega, DateTime.UtcNow);

        Assert.Equal(2, acuses.Count);
        Assert.Equal("delivered", acuses[0].Estado);
        Assert.Equal("failed", acuses[1].Estado);
        Assert.Equal("131047", acuses[1].CodigoError);
        Assert.Contains("Ventana cerrada", acuses[1].DescripcionError);
    }

    [Fact]
    public void Un_evento_sin_mensajes_no_produce_nada()
    {
        Assert.Empty(InterpreteWebhookMeta.Mensajes(PayloadsDePrueba.SinMensajes, DateTime.UtcNow));
        Assert.Empty(InterpreteWebhookMeta.Estados(PayloadsDePrueba.SinMensajes, DateTime.UtcNow));
    }

    [Fact]
    public void Un_cuerpo_ilegible_no_revienta_el_webhook()
    {
        // Reintentarlo daria el mismo resultado; lo correcto es descartarlo y registrarlo.
        Assert.Empty(InterpreteWebhookMeta.Mensajes("{ esto no es json valido", DateTime.UtcNow));
        Assert.Empty(InterpreteWebhookMeta.Mensajes("", DateTime.UtcNow));
    }

    [Fact]
    public void Los_estados_del_proveedor_cubren_todo_el_avance_del_acuse()
    {
        // El orden del enum es lo que permite ignorar acuses atrasados en MensajeService.
        Assert.True(EstadoEntrega.Enviado < EstadoEntrega.Entregado);
        Assert.True(EstadoEntrega.Entregado < EstadoEntrega.Leido);
    }
}
