namespace RRHH.WhatsApp.Domain.Interfaces;

/// <summary>
/// Datos personales tal como llegan del formulario, antes de que exista el postulante.
/// <para>
/// El dossier proponia <c>RegistrarDesdeFormulario(JobFormsRespuesta)</c>, pero esa entidad ya
/// lleva <c>PostulanteId</c>: para armarla hay que haber creado antes a la persona que este
/// metodo justamente crea. Ver docs/decisiones.md.
/// </para>
/// </summary>
public sealed record DatosPostulanteFormulario(
    string Dni,
    string? NombreCompleto,
    string? TelefonoE164,
    string? Email);

/// <summary>Envio completo del formulario, ya normalizado e independiente de Google Forms.</summary>
public sealed record EnvioJobForms(
    Guid Token,
    DatosPostulanteFormulario Postulante,
    string DatosJson,
    string? CvUrl,
    bool ConsentimientoAceptado);
