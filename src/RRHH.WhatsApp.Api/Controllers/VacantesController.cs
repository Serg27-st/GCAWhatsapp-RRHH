using RRHH.WhatsApp.Api.Seguridad;
using Microsoft.AspNetCore.Mvc;
using RRHH.WhatsApp.Api.Mapeo;
using RRHH.WhatsApp.Contracts.Administracion;
using RRHH.WhatsApp.Contracts.Bandeja;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Api.Controllers;

/// <summary>
/// Vacantes (HC) y su tablero. Sin estas rutas el estado de un HC nunca cambiaria a Cerrada y la
/// Regla 20 no podria activarse en la practica.
/// <para>
/// Crear, cerrar y configurar una vacante es de quien trabaja la cuenta —titular o respaldo—, que
/// es quien la abre y le arma el formulario; Sistemas, como soporte (V23). Una politica por rol no
/// sabe de que cuenta es cada vacante, asi que eso se decide aca.
/// </para>
/// </summary>
[ApiController]
[Route("hc")]
public sealed class VacantesController(
    ICuentaService cuentas,
    IPostulacionService postulaciones,
    ILogger<VacantesController> log) : ControllerBase
{
    /// <summary>Vacantes abiertas de una cuenta, que es lo que el bot ofrece en el menu.</summary>
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] int cuentaId, CancellationToken ct)
    {
        var vacantes = await cuentas.ListarVacantesAbiertasAsync(cuentaId, ct);

        return Ok(vacantes.Select(AResumen));
    }

    [HttpPost]
    public async Task<IActionResult> Crear(
        [FromBody] PeticionCrearVacante peticion, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(peticion.Titulo))
            return BadRequest(new { motivo = "La vacante necesita un titulo." });

        if (await RechazarSiNoTrabajaLaCuentaAsync(peticion.CuentaId, ct) is { } rechazo)
            return rechazo;

        try
        {
            var vacante = await cuentas.CrearVacanteAsync(
                peticion.CuentaId, peticion.Titulo.Trim(), peticion.UrlJobForms, User.AnalistaId(), ct);

            // Una vacante sin formulario se puede crear, pero el bot no podra enviarle el enlace
            // a nadie. Se avisa aca en vez de dejar que falle recien al primer postulante.
            if (string.IsNullOrWhiteSpace(vacante.UrlJobForms))
            {
                log.LogWarning(
                    "La vacante {HcId} se creo sin UrlJobForms: el bot no podra enviar su enlace.", vacante.HcId);
            }

            return CreatedAtAction(nameof(Listar), new { cuentaId = vacante.CuentaId }, AResumen(vacante));
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { motivo = ex.Message });
        }
    }

    /// <summary>Regla 20: al cerrarla, su enlace deja de servir y el bot deja de ofrecerla.</summary>
    [HttpPatch("{id:int}/cerrar")]
    public async Task<IActionResult> Cerrar(int id, CancellationToken ct)
    {
        var vacante = await cuentas.ObtenerVacanteAsync(id, ct);

        if (vacante is null)
            return NotFound(new { motivo = $"No existe la vacante {id}." });

        if (await RechazarSiNoTrabajaLaCuentaAsync(vacante.CuentaId, ct) is { } rechazo)
            return rechazo;

        try
        {
            await cuentas.CerrarVacanteAsync(id, User.AnalistaId(), ct);

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { motivo = ex.Message });
        }
    }


    /// <summary>
    /// Campos opcionales configurados para la vacante. Los consulta el analista para editarlos y
    /// el formulario para saber que preguntar de mas.
    /// </summary>
    [HttpGet("{id:int}/campos")]
    public async Task<IActionResult> Campos(int id, CancellationToken ct)
    {
        if (await cuentas.ObtenerVacanteAsync(id, ct) is null)
            return NotFound(new { motivo = $"No existe la vacante {id}." });

        var campos = await cuentas.ListarCamposOpcionalesAsync(id, ct);

        return Ok(campos.Select(c => new CampoOpcional(c.NombreCampo, c.Tipo.ToString(), c.Activo)));
    }

    /// <summary>
    /// Reemplaza el catalogo completo de campos opcionales de la vacante. Los tipos validos son
    /// los de <see cref="TipoCampoOpcional"/>.
    /// </summary>
    [HttpPut("{id:int}/campos")]
    public async Task<IActionResult> GuardarCampos(
        int id,
        [FromBody] IReadOnlyList<CampoOpcional> campos,
        CancellationToken ct)
    {
        var vacante = await cuentas.ObtenerVacanteAsync(id, ct);

        if (vacante is null)
            return NotFound(new { motivo = $"No existe la vacante {id}." });

        if (await RechazarSiNoTrabajaLaCuentaAsync(vacante.CuentaId, ct) is { } rechazo)
            return rechazo;

        var convertidos = new List<HcCampoOpcional>();

        foreach (var campo in campos)
        {
            if (!Enum.TryParse<TipoCampoOpcional>(campo.Tipo, ignoreCase: true, out var tipo))
            {
                return BadRequest(new
                {
                    motivo = $"Tipo '{campo.Tipo}' desconocido. Validos: " +
                             string.Join(", ", Enum.GetNames<TipoCampoOpcional>())
                });
            }

            convertidos.Add(new HcCampoOpcional
            {
                HcId = id,
                NombreCampo = campo.NombreCampo,
                Tipo = tipo,
                Activo = campo.Activo
            });
        }

        try
        {
            await cuentas.ReemplazarCamposOpcionalesAsync(id, convertidos, User.AnalistaId(), ct);

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { motivo = ex.Message });
        }
    }

    /// <summary>Regla 13: el tablero de la vacante, con todas sus columnas.</summary>
    [HttpGet("{id:int}/tablero")]
    public async Task<IActionResult> Tablero(int id, CancellationToken ct)
    {
        // El titulo se busca en la vacante y no se deduce de las tarjetas: un tablero recien
        // creado no tiene ninguna, y aun asi tiene que poder encabezarse.
        var vacante = await cuentas.ObtenerVacanteAsync(id, ct);

        // Regla 4 por cuenta: el tablero muestra nombre y DNI de cada postulante, asi que lo ven el
        // titular y el respaldo de la cuenta, y Sistemas. Vacante inexistente y ajena responden
        // igual, para que el id no sirva para averiguar que vacantes tienen otras cuentas.
        var nivel = vacante is null
            ? NivelAcceso.Ninguno
            : await cuentas.ObtenerAccesoAsync(vacante.CuentaId, User.AnalistaId(), ct);

        if (ResultadoAcceso.Evaluar(nivel, actua: false, "el tablero") is { } rechazo)
            return rechazo;

        var etapas = await postulaciones.ListarEtapasAsync(ct);
        var tarjetas = await postulaciones.ObtenerTableroAsync(id, ct);

        var porEtapa = tarjetas.ToLookup(p => p.EtapaKanbanId);

        // Se devuelven todas las etapas, incluso vacias: una columna sin tarjetas sigue siendo un
        // destino valido al arrastrar.
        var columnas = etapas
            .Select(e => new EtapaTablero(
                e.EtapaId, e.Nombre, e.Orden,
                [.. porEtapa[e.EtapaId].Select(p => p.AResumen())]))
            .ToList();

        return Ok(new TableroKanban(id, vacante!.Titulo, columnas));
    }

    /// <summary>Sin formulario el bot no puede mandar el enlace (Regla 9): la pantalla lo marca.</summary>
    private static VacanteResumen AResumen(Hc vacante) =>
        new(vacante.HcId, vacante.CuentaId, vacante.Titulo, vacante.Estado.ToString(),
            !string.IsNullOrWhiteSpace(vacante.UrlJobForms));

    /// <summary>
    /// Nulo si quien pide trabaja la cuenta o es Sistemas. Aca un 403 no revela nada: la lista de
    /// cuentas y sus vacantes abiertas es visible para todos.
    /// </summary>
    private async Task<IActionResult?> RechazarSiNoTrabajaLaCuentaAsync(int cuentaId, CancellationToken ct)
    {
        if (User.EsSistemas())
            return null;

        var nivel = await cuentas.ObtenerAccesoAsync(cuentaId, User.AnalistaId(), ct);

        return nivel == NivelAcceso.Total
            ? null
            : StatusCode(StatusCodes.Status403Forbidden, new
            {
                motivo = "Las vacantes de una cuenta las manejan su titular, su respaldo o Sistemas."
            });
    }
}
