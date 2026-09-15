using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Tests.Proveedores;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// Regla 15 en el único punto donde tiene efecto real: la respuesta del analista.
/// <para>
/// Hasta que existió este camino, la regla estaba escrita y probada aislada pero nada construía
/// nunca un contexto de envío saliente, así que nunca corría.
/// </para>
/// </summary>
public class EnvioAnalistaTests : IDisposable
{
    private readonly EntornoDeReglas _entorno = new();

    private async Task<int> ConHiloAtendidoAsync()
    {
        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);
        await _entorno.ConsumirOutboxAsync();

        return (await _entorno.Db.Conversaciones.FirstAsync()).ConversacionId;
    }

    private async Task CerrarVentanaAsync(int conversacionId)
    {
        var conversacion = await _entorno.Db.Conversaciones.FirstAsync(c => c.ConversacionId == conversacionId);

        conversacion.FechaUltimoMensajeEntrante = _entorno.Ahora.AddHours(-30);

        await _entorno.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Dentro_de_la_ventana_el_analista_responde_con_texto_libre()
    {
        var id = await ConHiloAtendidoAsync();

        var resultado = await _entorno.Envio.ResponderAsync(
            id, EntornoDeReglas.TitularId, "Hola, te escribo por la vacante.", null, null);

        Assert.True(resultado.Enviado);
        Assert.NotNull(resultado.MensajeId);

        var mensaje = await _entorno.Db.Mensajes.AsNoTracking()
            .FirstAsync(m => m.MensajeId == resultado.MensajeId);

        // El mensaje queda atribuido a quien lo escribio, no al bot.
        Assert.Equal(EntornoDeReglas.TitularId, mensaje.AnalistaId);
        Assert.Equal(DireccionMensaje.Saliente, mensaje.Direccion);
    }

    [Fact]
    public async Task Responder_detiene_el_reloj_del_escalamiento_de_la_Regla_2()
    {
        // Sin esta marca el Worker escalaria una conversacion que el analista acaba de atender.
        var id = await ConHiloAtendidoAsync();

        await _entorno.Envio.ResponderAsync(id, EntornoDeReglas.TitularId, "Ya te respondo.", null, null);

        var conversacion = await _entorno.Db.Conversaciones.AsNoTracking()
            .FirstAsync(c => c.ConversacionId == id);

        Assert.NotNull(conversacion.FechaUltimaRespuestaAnalista);

        var pendientes = await _entorno.Conversaciones.ListarPendientesEscalamientoAsync(50);
        Assert.DoesNotContain(id, pendientes);
    }

    [Fact]
    public async Task Sin_optin_registrado_no_sale_nada()
    {
        // Es la causa de bloqueo que el proyecto existe para evitar (Seccion 2.4).
        var id = await ConHiloAtendidoAsync();

        var conversacion = await _entorno.Db.Conversaciones.FirstAsync(c => c.ConversacionId == id);
        conversacion.FechaOptIn = null;
        conversacion.OrigenOptIn = null;
        await _entorno.Db.SaveChangesAsync();

        var antes = _entorno.Proveedor.Enviados.Count;

        var resultado = await _entorno.Envio.ResponderAsync(
            id, EntornoDeReglas.TitularId, "Hola", null, null);

        Assert.False(resultado.Enviado);
        Assert.False(resultado.RequierePlantilla);
        Assert.Contains("opt-in", resultado.Motivo!);
        Assert.Equal(antes, _entorno.Proveedor.Enviados.Count);
    }

    [Fact]
    public async Task Fuera_de_la_ventana_el_texto_libre_se_rechaza_pidiendo_plantilla()
    {
        var id = await ConHiloAtendidoAsync();
        await CerrarVentanaAsync(id);

        var antes = _entorno.Proveedor.Enviados.Count;

        var resultado = await _entorno.Envio.ResponderAsync(
            id, EntornoDeReglas.TitularId, "Hola de nuevo", null, null);

        Assert.False(resultado.Enviado);
        Assert.True(resultado.RequierePlantilla);
        Assert.Equal(antes, _entorno.Proveedor.Enviados.Count);
    }

    [Fact]
    public async Task Fuera_de_la_ventana_si_sale_una_plantilla_aprobada()
    {
        var plantilla = await _entorno.Db.Plantillas
            .FirstAsync(p => p.Clave == ClavesPlantilla.ReaperturaConversacion);

        plantilla.Activa = true;
        await _entorno.Db.SaveChangesAsync();

        var id = await ConHiloAtendidoAsync();
        await CerrarVentanaAsync(id);

        var resultado = await _entorno.Envio.ResponderAsync(
            id, EntornoDeReglas.TitularId, null, ClavesPlantilla.ReaperturaConversacion, ["Maria"]);

        Assert.True(resultado.Enviado);
        Assert.Contains(_entorno.Proveedor.Enviados,
            e => e.Tipo == "plantilla" && e.Detalle == ClavesPlantilla.ReaperturaConversacion);
    }

    [Fact]
    public async Task Una_plantilla_sin_aprobar_se_rechaza_diciendo_por_que()
    {
        var id = await ConHiloAtendidoAsync();

        var resultado = await _entorno.Envio.ResponderAsync(
            id, EntornoDeReglas.TitularId, null, ClavesPlantilla.CierreCortesia, ["Maria", "Operario"]);

        Assert.False(resultado.Enviado);
        Assert.Contains("Meta", resultado.Motivo!);
    }

    [Fact]
    public async Task No_se_manda_una_plantilla_con_la_cantidad_de_parametros_equivocada()
    {
        // Meta rechaza el envio y cuenta el intento fallido; conviene atajarlo antes de salir.
        var plantilla = await _entorno.Db.Plantillas
            .FirstAsync(p => p.Clave == ClavesPlantilla.CierreCortesia);

        plantilla.Activa = true;
        await _entorno.Db.SaveChangesAsync();

        var id = await ConHiloAtendidoAsync();

        var resultado = await _entorno.Envio.ResponderAsync(
            id, EntornoDeReglas.TitularId, null, ClavesPlantilla.CierreCortesia, ["solo uno"]);

        Assert.False(resultado.Enviado);
        Assert.Contains("parametro", resultado.Motivo!);
    }

    [Fact]
    public async Task El_intento_fuera_de_ventana_queda_registrado_para_auditoria()
    {
        var id = await ConHiloAtendidoAsync();
        await CerrarVentanaAsync(id);

        await _entorno.Envio.ResponderAsync(id, EntornoDeReglas.TitularId, "Hola", null, null);

        var registrado = await _entorno.Db.EventosSistema
            .AnyAsync(e => e.Tipo == "EnvioRequierePlantilla");

        Assert.True(registrado);
    }

    public void Dispose() => _entorno.Dispose();
}
