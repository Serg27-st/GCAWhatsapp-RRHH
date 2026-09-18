using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Tests.Casos;

namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// E19, parte de entrada (FUN-09, A3): el mismo DNI en dos cuentas manda por un único hilo de
/// WhatsApp (V1). Cuando escribe sin decir por cuál, el bot no puede adivinar: le ofrece sus
/// procesos por nombre. Antes lo mandaba a la cuenta que hubiera quedado en el contexto.
/// </summary>
public class E19MultiCuentaTests : IDisposable
{
    private const int OtraCuentaId = 8;
    private const int OtroAnalistaId = 12;
    private const int OtroHcId = 9;

    private readonly ArnesEscenario _arnes = new();

    private string[] UltimoMenu() =>
        _arnes.Enviados().Last(e => e.Tipo is "botones" or "lista").Detalle.Split(',');

    /// <summary>Deja al postulante con un proceso vivo en cada cuenta y el hilo sin contexto.</summary>
    private async Task<int> EnDosCuentasAsync()
    {
        _arnes.Entorno.Db.Cuentas.Add(new Cuenta { CuentaId = OtraCuentaId, Nombre = "Intradevco", Activo = true });

        _arnes.Entorno.Db.Analistas.Add(new Analista
        {
            AnalistaId = OtroAnalistaId, Nombre = "Rosa Diaz", Email = "rosa@gca.pe", Activo = true
        });

        _arnes.Entorno.Db.AnalistaCuentas.Add(new AnalistaCuenta
        {
            AnalistaCuentaId = 3, AnalistaId = OtroAnalistaId, CuentaId = OtraCuentaId, EsBackup = false
        });

        _arnes.Entorno.Db.Hcs.Add(new Hc
        {
            HcId = OtroHcId,
            CuentaId = OtraCuentaId,
            Titulo = "Envasador",
            UrlJobForms = "https://forms.gle/envasador",
            Estado = EstadoHc.Abierta,
            FechaCreacion = _arnes.Entorno.Ahora
        });

        await _arnes.Entorno.Db.SaveChangesAsync();

        // Primera postulación, por el circuito real: elige Alicorp y completa el formulario.
        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Boton("cuenta_7", "Alicorp"),
            new ConsumirOutbox(),
            new Despachar(),
            new Formulario(),
            new ConsumirOutbox(),
            new Despachar());

        var conversacion = await _arnes.ConversacionAsync();

        // Segunda: la misma persona en la otra cuenta, como cuando postula a dos avisos.
        _arnes.Entorno.Db.Postulaciones.Add(new Postulacion
        {
            PostulanteId = conversacion.PostulanteId!.Value,
            HcId = OtroHcId,
            CuentaId = OtraCuentaId,
            AnalistaAsignadoId = OtroAnalistaId,
            EtapaKanbanId = 1,
            Estado = EstadoPostulacion.EnProceso,
            FechaCreacion = _arnes.Entorno.Ahora,
            FechaUltimaActividad = _arnes.Entorno.Ahora
        });

        await _arnes.Entorno.Db.SaveChangesAsync();

        return conversacion.ConversacionId;
    }

    [Fact]
    public async Task Con_dos_procesos_vivos_el_bot_pregunta_por_cual_escribe()
    {
        var id = await EnDosCuentasAsync();

        // Diez días después: el contexto ya no es confiable (A13) y hay más de una cuenta viva.
        await _arnes.ConversarAsync(
            new Avanzar(TimeSpan.FromDays(10)),
            new Entrante("Hola, consulta"),
            new ConsumirOutbox(),
            new Despachar());

        var menu = UltimoMenu();

        Assert.Equal(2, menu.Count(b => b.StartsWith(IdsBoton.PrefijoProceso)));
        Assert.Contains(IdsBoton.OtraEmpresa, menu);
    }

    [Fact]
    public async Task Elegir_un_proceso_lo_deja_con_la_cuenta_y_el_analista_de_esa_postulacion()
    {
        await EnDosCuentasAsync();

        await _arnes.ConversarAsync(
            new Avanzar(TimeSpan.FromDays(10)),
            new Entrante("Hola, consulta"),
            new ConsumirOutbox(),
            new Despachar());

        var deLaOtraCuenta = await _arnes.Entorno.Db.Postulaciones.AsNoTracking()
            .FirstAsync(p => p.CuentaId == OtraCuentaId);

        await _arnes.ConversarAsync(
            new Boton(IdsBoton.ParaProceso(deLaOtraCuenta.PostulacionId), "Intradevco"),
            new ConsumirOutbox(),
            new Despachar());

        var conversacion = await _arnes.ConversacionAsync();

        Assert.Equal(OtraCuentaId, conversacion.CuentaContextoId);
        Assert.Equal(OtroAnalistaId, conversacion.AnalistaAtendiendoId);
    }

    /// <summary>«Otra empresa» es la salida: quiere postular a algo nuevo, no seguir lo que ya tiene.</summary>
    [Fact]
    public async Task Otra_empresa_lleva_al_menu_de_empresas()
    {
        await EnDosCuentasAsync();

        await _arnes.ConversarAsync(
            new Avanzar(TimeSpan.FromDays(10)),
            new Entrante("Hola, consulta"),
            new ConsumirOutbox(),
            new Despachar(),
            new Boton(IdsBoton.OtraEmpresa, "Otra empresa"),
            new ConsumirOutbox(),
            new Despachar());

        var conversacion = await _arnes.ConversacionAsync();

        Assert.Null(conversacion.CuentaContextoId);
        Assert.Contains(UltimoMenu(), b => b.StartsWith(IdsBoton.PrefijoCuenta));
    }


    /// <summary>
    /// E19, parte de salida (FUN-09, R11): lo que el analista escribe llega por el mismo hilo que los
    /// mensajes de la otra empresa. Sin decir de cuál habla, una citación es una cita sin dirección.
    /// </summary>
    [Fact]
    public async Task La_respuesta_del_analista_dice_de_que_empresa_habla()
    {
        await EnDosCuentasAsync();

        await _arnes.ConversarAsync(
            new RespuestaAnalista("Te esperamos el lunes a las 9.", EntornoDeReglas.TitularId));

        var enviado = _arnes.Enviados().Last(e => e.Tipo == "texto");

        Assert.StartsWith("[Alicorp · Operario de produccion]", enviado.Detalle);
    }

    /// <summary>Con un solo proceso vivo el prefijo sería ruido en cada mensaje.</summary>
    [Fact]
    public async Task Con_un_solo_proceso_la_respuesta_va_sin_prefijo()
    {
        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Boton("cuenta_7", "Alicorp"),
            new ConsumirOutbox(),
            new Despachar(),
            new Formulario(),
            new ConsumirOutbox(),
            new Despachar(),
            new RespuestaAnalista("Te esperamos el lunes a las 9.", EntornoDeReglas.TitularId));

        Assert.Equal("Te esperamos el lunes a las 9.", _arnes.Enviados().Last(e => e.Tipo == "texto").Detalle);
    }
    /// <summary>Con un solo proceso vivo no hay nada que desambiguar: eso lo resuelve la R01 (FUN-08).</summary>
    [Fact]
    public async Task Con_un_solo_proceso_vivo_no_pregunta_nada()
    {
        await _arnes.ConversarAsync(
            new Entrante("Hola"),
            new ConsumirOutbox(),
            new Boton("cuenta_7", "Alicorp"),
            new ConsumirOutbox(),
            new Despachar(),
            new Formulario(),
            new ConsumirOutbox(),
            new Despachar());

        await _arnes.ConversarAsync(
            new Avanzar(TimeSpan.FromDays(10)),
            new Entrante("Hola, consulta"),
            new ConsumirOutbox(),
            new Despachar());

        Assert.DoesNotContain(_arnes.Enviados(), e => e.Detalle.Contains(IdsBoton.PrefijoProceso));
    }

    public void Dispose() => _arnes.Dispose();
}
