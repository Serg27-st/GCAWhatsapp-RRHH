using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Tests.Proveedores;

namespace RRHH.WhatsApp.Tests.Casos;

/// <summary>
/// El circuito completo del JobForms: el bot manda el enlace, el postulante lo completa, y el
/// sistema lo devuelve al chat con su postulacion creada (Regla 9, Seccion 6).
/// </summary>
public class RecepcionJobFormsTests : IDisposable
{
    private readonly EntornoDeReglas _entorno = new();

    /// <summary>Deja el hilo con el enlace ya enviado y devuelve el token de la invitacion.</summary>
    private async Task<Guid> ConEnlaceEnviadoAsync()
    {
        await _entorno.IngresarAsync(PayloadsDePrueba.RespuestaDeBoton);
        await _entorno.ConsumirOutboxAsync();

        var invitacion = await _entorno.Db.JobFormsInvitaciones.AsNoTracking().FirstAsync();

        return invitacion.Token;
    }

    private static EnvioJobForms Envio(Guid token, bool consentimiento = true) =>
        new(token,
            new DatosPostulanteFormulario("45678912", "Maria Quispe", "+51987654321", "maria@correo.pe"),
            """{"experiencia":"2 anos"}""",
            "https://drive.google.com/file/abc",
            consentimiento);

    [Fact]
    public async Task Un_envio_valido_crea_al_postulante_su_postulacion_y_la_respuesta()
    {
        var token = await ConEnlaceEnviadoAsync();

        var resultado = await _entorno.RecepcionFormulario.ProcesarAsync(Envio(token));

        var postulante = await _entorno.Db.Postulantes.AsNoTracking().FirstAsync();
        Assert.Equal("45678912", postulante.Dni);
        Assert.Equal(resultado.PostulanteId, postulante.PostulanteId);

        var postulacion = await _entorno.Db.Postulaciones.AsNoTracking().FirstAsync();
        Assert.Equal(EstadoPostulacion.EnProceso, postulacion.Estado);
        Assert.Equal(EntornoDeReglas.CuentaId, postulacion.CuentaId);
        // Regla 1: la tarjeta nace con el analista dueno de la cuenta.
        Assert.Equal(EntornoDeReglas.TitularId, postulacion.AnalistaAsignadoId);

        var respuesta = await _entorno.Db.JobFormsRespuestas.AsNoTracking().FirstAsync();
        Assert.Equal(resultado.RespuestaId, respuesta.RespuestaId);
    }

    [Fact]
    public async Task El_hilo_de_WhatsApp_queda_vinculado_a_la_persona()
    {
        // Hasta aca la conversacion solo conocia un telefono (desviacion V2 de decisiones.md).
        var token = await ConEnlaceEnviadoAsync();

        var conversacionAntes = await _entorno.Db.Conversaciones.AsNoTracking().FirstAsync();
        Assert.Null(conversacionAntes.PostulanteId);

        await _entorno.RecepcionFormulario.ProcesarAsync(Envio(token));

        var conversacion = await _entorno.Db.Conversaciones.AsNoTracking().FirstAsync();
        Assert.NotNull(conversacion.PostulanteId);
    }

    [Fact]
    public async Task La_invitacion_queda_completada_y_deja_de_generar_recordatorios()
    {
        var token = await ConEnlaceEnviadoAsync();

        await _entorno.RecepcionFormulario.ProcesarAsync(Envio(token));

        var invitacion = await _entorno.Db.JobFormsInvitaciones.AsNoTracking().FirstAsync();

        Assert.True(invitacion.Completado);
        Assert.NotNull(invitacion.FechaCompletado);

        // Regla 9: sin invitacion pendiente, el barrido ya no tiene nada que recordar.
        var pendientes = await _entorno.Invitaciones.ListarPendientesRecordatorioAsync(TimeSpan.Zero);
        Assert.Empty(pendientes);
    }

    [Fact]
    public async Task Se_confirma_por_WhatsApp_que_el_formulario_llego()
    {
        var plantilla = await _entorno.Db.Plantillas
            .FirstAsync(p => p.Clave == ClavesPlantilla.ConfirmacionJobForms);

        plantilla.Activa = true;
        await _entorno.Db.SaveChangesAsync();

        var token = await ConEnlaceEnviadoAsync();

        await _entorno.RecepcionFormulario.ProcesarAsync(Envio(token));

        // La confirmacion no sale del request: viaja por la outbox, como el resto del flujo.
        await _entorno.ConsumirOutboxAsync();

        var confirmacion = Assert.Single(
            _entorno.Proveedor.Enviados,
            e => e.Tipo == "plantilla" && e.Detalle == ClavesPlantilla.ConfirmacionJobForms);

        Assert.NotNull(confirmacion);
    }

    [Fact]
    public async Task Regla_17_sin_consentimiento_no_se_guarda_nada()
    {
        var token = await ConEnlaceEnviadoAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _entorno.RecepcionFormulario.ProcesarAsync(Envio(token, consentimiento: false)));

        Assert.Empty(_entorno.Db.JobFormsRespuestas);
        Assert.Empty(_entorno.Db.Postulaciones);
    }

    [Fact]
    public async Task Regla_20_una_vacante_cerrada_entre_medio_rechaza_el_envio()
    {
        // El postulante puede tardar horas en llenar el formulario, y en ese lapso el analista
        // puede cerrar la vacante.
        var token = await ConEnlaceEnviadoAsync();

        var vacante = await _entorno.Db.Hcs.FirstAsync(h => h.HcId == 1);
        vacante.Estado = EstadoHc.Cerrada;
        await _entorno.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _entorno.RecepcionFormulario.ProcesarAsync(Envio(token)));

        Assert.Empty(_entorno.Db.Postulaciones);
    }

    [Fact]
    public async Task Un_token_desconocido_se_rechaza()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _entorno.RecepcionFormulario.ProcesarAsync(Envio(Guid.NewGuid())));
    }

    [Fact]
    public async Task Regla_17_se_sella_la_version_del_aviso_de_privacidad_vigente()
    {
        var token = await ConEnlaceEnviadoAsync();

        await _entorno.RecepcionFormulario.ProcesarAsync(Envio(token));

        var respuesta = await _entorno.Db.JobFormsRespuestas.AsNoTracking().FirstAsync();

        Assert.Equal("2026-08-v1", respuesta.VersionAvisoPrivacidad);
        Assert.NotNull(respuesta.FechaConsentimiento);
    }

    [Fact]
    public async Task El_mismo_DNI_postulando_dos_veces_no_se_duplica()
    {
        // Regla 9: el DNI es el identificador, no el telefono ni la postulacion.
        var token = await ConEnlaceEnviadoAsync();

        await _entorno.RecepcionFormulario.ProcesarAsync(Envio(token));
        await _entorno.RecepcionFormulario.ProcesarAsync(Envio(token));

        Assert.Single(_entorno.Db.Postulantes);
        Assert.Single(_entorno.Db.Postulaciones);
    }

    public void Dispose() => _entorno.Dispose();
}
