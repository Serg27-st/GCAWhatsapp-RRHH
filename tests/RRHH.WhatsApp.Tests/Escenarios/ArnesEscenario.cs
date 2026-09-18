using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Application.Casos;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Proveedores;
using RRHH.WhatsApp.Tests.Casos;

namespace RRHH.WhatsApp.Tests.Escenarios;

/// <summary>
/// Recorre una conversación de punta a punta sobre <see cref="EntornoDeReglas"/>: webhook, outbox,
/// reglas, barrido y respuesta del analista, con el reloj simulado (ARQ-12).
/// <para>
/// Las pruebas por regla miran una decisión aislada. Lo que se escapó en la auditoría —el aviso
/// fuera de horario que nunca sale (C1), el menú repetido tras completar el formulario (C2), el hilo
/// reactivado que queda invisible (AL10)— solo aparece al encadenar pasos, que es lo que hace esto.
/// </para>
/// </summary>
internal sealed class ArnesEscenario : IDisposable
{
    /// <summary>El teléfono del postulante cuando el paso no dice otro.</summary>
    public const string TelefonoPorDefecto = "+51987654321";

    public EntornoDeReglas Entorno { get; } = new();

    public async Task ConversarAsync(params PasoConversacion[] pasos)
    {
        foreach (var paso in pasos)
        {
            switch (paso)
            {
                case Entrante e:
                    await IngresarAsync(PayloadTexto(e.Telefono ?? TelefonoPorDefecto, e.Texto));
                    break;

                case Boton b:
                    await IngresarAsync(PayloadBoton(b.Telefono ?? TelefonoPorDefecto, b.IdBoton, b.Titulo));
                    break;

                case Medio m:
                    await IngresarAsync(PayloadMedio(m));
                    break;

                case Avanzar a:
                    await Entorno.AvanzarAsync(a.Lapso);
                    break;

                case Formulario f:
                    await CompletarFormularioAsync(f);
                    break;

                case RespuestaAnalista r:
                    await ResponderAsync(r);
                    break;

                case Barrido:
                    await BarrerAsync();
                    break;

                case ConsumirOutbox:
                    await Entorno.ConsumirOutboxAsync(despachar: false);
                    break;

                case Despachar:
                    // Una vuelta del despachador del Worker (V29), con el mismo timeout de Enviando.
                    await Entorno.DespacharAsync();
                    break;

                case Acuse a:
                    await IngresarAsync(await PayloadAcuseAsync(a));
                    break;

                case DescargarAdjuntos:
                    await Entorno.DescargaAdjuntos.ProcesarAsync(EntornoDeReglas.ParametrosDescarga);
                    break;

                default:
                    throw new NotSupportedException($"Paso de conversación desconocido: {paso.GetType().Name}.");
            }
        }
    }

    /// <summary>Todo lo que el bot o el analista le mandaron a ese teléfono, en orden.</summary>
    public IReadOnlyList<EnvioSimulado> Enviados(string? telefono = null) =>
        [.. Entorno.Proveedor.Enviados.Where(e => e.Telefono == (telefono ?? TelefonoPorDefecto))];

    public async Task<EstadoConversacion> EstadoConversacion(string? telefono = null) =>
        (await ConversacionAsync(telefono)).Estado;

    public async Task<Conversacion> ConversacionAsync(string? telefono = null) =>
        await Entorno.Db.Conversaciones.AsNoTracking()
            .FirstAsync(c => c.TelefonoE164 == (telefono ?? TelefonoPorDefecto));

    /// <summary>Los avisos para analistas que salen por el canal en vivo (V16).</summary>
    public async Task<IReadOnlyList<EventoSistema>> Notificaciones() =>
        await Entorno.Db.EventosSistema.AsNoTracking()
            .Where(e => e.Tipo == TiposEvento.AnalistaNotificado)
            .OrderBy(e => e.EventoId)
            .ToListAsync();

    /// <summary>
    /// Lo que una persona tiene que resolver: plantilla sin aprobar, vacante sin formulario, menú sin
    /// opciones (V32). Las abiertas, agrupadas por tipo y clave.
    /// </summary>
    public async Task<IReadOnlyList<AlertaOperativa>> Alertas() =>
        await Entorno.Db.AlertasOperativas.AsNoTracking()
            .Where(a => a.FechaResuelta == null)
            .OrderBy(a => a.AlertaId)
            .ToListAsync();

    /// <summary>
    /// Horario general de lunes a viernes, 09:00 a 18:00 de Lima. Sin tramos cargados el calendario
    /// asume jornada permanente, y entonces la Regla 3 no tiene nada fuera de lo cual avisar.
    /// </summary>
    public async Task ConHorarioComercialAsync()
    {
        for (var dia = DayOfWeek.Monday; dia <= DayOfWeek.Friday; dia++)
        {
            Entorno.Db.HorariosAtencion.Add(new HorarioAtencion
            {
                CuentaId = null,
                DiaSemana = dia,
                HoraInicio = new TimeOnly(9, 0),
                HoraFin = new TimeOnly(18, 0)
            });
        }

        await Entorno.Db.SaveChangesAsync();
    }

    private Task IngresarAsync(string payload) =>
        Entorno.Recepcion.ProcesarAsync(payload, new Dictionary<string, string>());

    private async Task CompletarFormularioAsync(Formulario f)
    {
        var conversacion = await ConversacionAsync(f.Telefono);

        var invitacion = await Entorno.Db.JobFormsInvitaciones.AsNoTracking()
            .Where(i => i.ConversacionId == conversacion.ConversacionId)
            .OrderByDescending(i => i.InvitacionId)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("El escenario completa un formulario que el bot nunca envió.");

        await Entorno.RecepcionFormulario.ProcesarAsync(new EnvioJobForms(
            invitacion.Token,
            new DatosPostulanteFormulario(f.Dni, f.NombreCompleto, conversacion.TelefonoE164, null),
            "{}",
            null,
            f.Consentimiento));
    }

    private async Task ResponderAsync(RespuestaAnalista r)
    {
        var conversacion = await ConversacionAsync(r.Telefono);

        var analistaId = r.AnalistaId ?? conversacion.AnalistaAtendiendoId
            ?? throw new InvalidOperationException("Nadie atiende la conversación: el paso tiene que decir qué analista responde.");

        await Entorno.Envio.ResponderAsync(conversacion.ConversacionId, analistaId, r.Texto, r.ClavePlantilla, null);
    }

    /// <summary>
    /// Una vuelta del barrido del Worker: por conversación y, después, por postulación (FUN-10,
    /// FUN-11). El Worker prefiltra candidatas; acá se recorre todo, que es equivalente y más simple.
    /// </summary>
    private async Task BarrerAsync()
    {
        var ids = await Entorno.Db.Conversaciones.AsNoTracking()
            .Select(c => c.ConversacionId)
            .ToListAsync();

        foreach (var id in ids)
            await Entorno.Barrido.ProcesarConversacionAsync(id);

        var postulaciones = await Entorno.Db.Postulaciones.AsNoTracking()
            .Select(p => p.PostulacionId)
            .ToListAsync();

        foreach (var id in postulaciones)
            await Entorno.Barrido.ProcesarPostulacionAsync(id);
    }

    private string PayloadTexto(string telefono, string texto) => Payload(telefono, new
    {
        type = "text",
        text = new { body = texto }
    });

    private string PayloadBoton(string telefono, string idBoton, string titulo) => Payload(telefono, new
    {
        type = "interactive",
        interactive = new
        {
            type = "button_reply",
            button_reply = new { id = idBoton, title = titulo }
        }
    });

    /// <summary>El id de medio es único por paso, como el de Meta: es lo que después se descarga.</summary>
    private string PayloadMedio(Medio m)
    {
        var medio = new JsonObject
        {
            ["id"] = $"medio.escenario.{Guid.NewGuid():N}",
            ["mime_type"] = m.MimeType
        };

        if (m.NombreArchivo is { } nombre)
            medio["filename"] = nombre;

        if (m.Leyenda is { } leyenda)
            medio["caption"] = leyenda;

        return Payload(m.Telefono ?? TelefonoPorDefecto, new JsonObject
        {
            ["type"] = m.Tipo,
            [m.Tipo] = medio
        });
    }

    /// <summary>
    /// La forma de la Cloud API (la misma que reenvía 360dialog), con un wamid único y el timestamp del
    /// reloj simulado: así la ventana de 24h y la actividad nacen con la fecha del escenario.
    /// </summary>
    private string Payload(string telefono, object contenido)
    {
        var numero = telefono.TrimStart('+');
        var mensaje = JsonSerializer.SerializeToNode(contenido)!.AsObject();

        mensaje["from"] = numero;
        mensaje["id"] = $"wamid.escenario.{Guid.NewGuid():N}";
        mensaje["timestamp"] = MarcaDeTiempo();

        return Envolver(new
        {
            messaging_product = "whatsapp",
            contacts = new[] { new { profile = new { name = "Maria Quispe" }, wa_id = numero } },
            messages = new[] { mensaje }
        });
    }

    /// <summary>
    /// El acuse va contra el id que el proveedor devolvió al enviar, que es lo único que Meta conoce
    /// del mensaje: por eso se busca el último saliente que llegó a salir.
    /// </summary>
    private async Task<string> PayloadAcuseAsync(Acuse a)
    {
        var conversacion = await ConversacionAsync(a.Telefono);

        var providerMessageId = await Entorno.Db.Mensajes.AsNoTracking()
            .Where(m => m.ConversacionId == conversacion.ConversacionId
                     && m.Direccion == DireccionMensaje.Saliente
                     && m.ProviderMessageId != null)
            .OrderByDescending(m => m.MensajeId)
            .Select(m => m.ProviderMessageId)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("El escenario acusa un mensaje que nunca salió.");

        var estado = new JsonObject
        {
            ["id"] = providerMessageId,
            ["status"] = a.Estado,
            ["timestamp"] = MarcaDeTiempo(),
            ["recipient_id"] = conversacion.TelefonoE164.TrimStart('+')
        };

        if (a.CodigoError is { } codigo)
        {
            estado["errors"] = new JsonArray(new JsonObject
            {
                ["code"] = codigo,
                ["title"] = a.TituloError ?? "Error"
            });
        }

        return Envolver(new
        {
            messaging_product = "whatsapp",
            statuses = new[] { estado }
        });
    }

    private string MarcaDeTiempo() => Entorno.Reloj.GetUtcNow().ToUnixTimeSeconds().ToString();

    private static string Envolver(object valor) => JsonSerializer.Serialize(new
    {
        @object = "whatsapp_business_account",
        entry = new[]
        {
            new
            {
                id = "102290129340398",
                changes = new[] { new { field = "messages", value = valor } }
            }
        }
    });

    public void Dispose() => Entorno.Dispose();
}
