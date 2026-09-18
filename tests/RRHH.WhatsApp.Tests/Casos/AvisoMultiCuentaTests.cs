using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Tests.Proveedores;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// Regla 6 sobre el circuito real (COR-10, AL9, P4): el aviso de que el postulante esta en varias
/// cuentas sale cuando hay algo nuevo que contar, no en cada mensaje que escribe.
/// </summary>
public class AvisoMultiCuentaTests : IDisposable
{
    private const int OtraCuentaId = 8;
    private const int OtroAnalistaId = 12;

    private readonly EntornoDeReglas _entorno = new();

    private static Dictionary<string, string> SinCabeceras() => [];

    private Task<int> AvisosAsync() =>
        _entorno.Db.EventosSistema.CountAsync(e => e.Tipo == TiposEvento.AnalistaNotificado);

    /// <summary>Un postulante identificado con un proceso vivo en otra cuenta, atendida por otro analista.</summary>
    private async Task<int> ConProcesoEnOtraCuentaAsync()
    {
        _entorno.Db.Cuentas.Add(new Cuenta { CuentaId = OtraCuentaId, Nombre = "Intradevco", Activo = true });

        _entorno.Db.Analistas.Add(new Analista
        {
            AnalistaId = OtroAnalistaId, Nombre = "Rosa Diaz", Email = "rosa@gca.pe", Activo = true
        });

        _entorno.Db.Hcs.Add(new Hc
        {
            HcId = 9,
            CuentaId = OtraCuentaId,
            Titulo = "Envasador",
            UrlJobForms = "https://forms.gle/envasador",
            Estado = EstadoHc.Abierta,
            FechaCreacion = _entorno.Ahora
        });

        var postulante = new Postulante { Dni = "45678912", FechaRegistro = _entorno.Ahora };
        _entorno.Db.Postulantes.Add(postulante);

        await _entorno.Db.SaveChangesAsync();

        _entorno.Db.Postulaciones.Add(new Postulacion
        {
            PostulanteId = postulante.PostulanteId,
            HcId = 9,
            CuentaId = OtraCuentaId,
            AnalistaAsignadoId = OtroAnalistaId,
            EtapaKanbanId = 1,
            Estado = EstadoPostulacion.EnProceso,
            FechaCreacion = _entorno.Ahora,
            FechaUltimaActividad = _entorno.Ahora
        });

        await _entorno.Db.SaveChangesAsync();

        return postulante.PostulanteId;
    }

    private async Task VincularAsync(int postulanteId)
    {
        var conversacion = await _entorno.Db.Conversaciones.FirstAsync();
        conversacion.PostulanteId = postulanteId;

        await _entorno.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Tres_mensajes_seguidos_dan_un_solo_aviso()
    {
        var postulanteId = await ConProcesoEnOtraCuentaAsync();

        // El boton de cuenta identifica la empresa y la Regla 1 asigna: ahi sale el aviso.
        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);
        await VincularAsync(postulanteId);
        await _entorno.ConsumirOutboxAsync();

        Assert.Equal(1, await AvisosAsync());

        foreach (var payload in new[] { PayloadsDePrueba.MensajeDeTexto, PayloadsDePrueba.RespuestaDeLista })
        {
            await _entorno.Recepcion.ProcesarAsync(payload, SinCabeceras());
            await _entorno.ConsumirOutboxAsync();
        }

        Assert.Equal(1, await AvisosAsync());
    }

    /// <summary>
    /// COR-10: al completar el formulario nace una postulacion en esta cuenta, y eso si es novedad
    /// para las dos partes: la Regla 6 pide que cada analista reciba su aviso.
    /// </summary>
    [Fact]
    public async Task Completar_el_formulario_avisa_a_los_analistas_de_las_dos_cuentas()
    {
        var postulanteId = await ConProcesoEnOtraCuentaAsync();

        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);
        await VincularAsync(postulanteId);
        await _entorno.ConsumirOutboxAsync();

        var invitacion = await _entorno.Db.JobFormsInvitaciones.AsNoTracking().FirstAsync();

        await _entorno.RecepcionFormulario.ProcesarAsync(new EnvioJobForms(
            invitacion.Token,
            new DatosPostulanteFormulario("45678912", "Maria Quispe", "+51987654321", null),
            "{}",
            null,
            ConsentimientoAceptado: true));

        await _entorno.ConsumirOutboxAsync();

        var destinatarios = await _entorno.Db.EventosSistema
            .Where(e => e.Tipo == TiposEvento.AnalistaNotificado)
            .Select(e => e.Payload)
            .ToListAsync();

        Assert.Contains(destinatarios, p => p.Contains($"\"analistaId\":{EntornoDeReglas.TitularId}"));
        Assert.Contains(destinatarios, p => p.Contains($"\"analistaId\":{OtroAnalistaId}"));
    }

    public void Dispose() => _entorno.Dispose();
}
