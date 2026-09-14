using RRHH.WhatsApp.Api.Seguridad;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RRHH.WhatsApp.Api.Mapeo;
using RRHH.WhatsApp.Contracts.Administracion;
using RRHH.WhatsApp.Contracts.Bandeja;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Interfaces;

namespace RRHH.WhatsApp.Api.Controllers;

/// <summary>
/// Catalogo de plantillas y horario de atencion. Son los dos ajustes que el area necesita poder
/// mirar y cambiar sin entrar a SQL Server.
/// </summary>
[ApiController]
public sealed class ConfiguracionController(
    IPlantillaService plantillas,
    IHorarioAtencionService horarios,
    IPostulacionService postulaciones,
    IConfiguracionReglasService configuracion,
    ILogger<ConfiguracionController> log) : ControllerBase
{
    /// <summary>
    /// Catalogo completo, incluidas las inactivas: mientras una plantilla no este aprobada por
    /// Meta, la regla que la necesita no puede enviar, y esta lista es donde se ve cual falta.
    /// </summary>
    [HttpGet("plantillas")]
    public async Task<IActionResult> Plantillas(CancellationToken ct)
    {
        var catalogo = await plantillas.ListarTodasAsync(ct);

        return Ok(catalogo.Select(p => p.AResumen()));
    }

    /// <summary>Columnas del tablero, para que la bandeja no las tenga escritas a mano.</summary>
    [HttpGet("etapas")]
    public async Task<IActionResult> Etapas(CancellationToken ct)
    {
        var etapas = await postulaciones.ListarEtapasAsync(ct);

        return Ok(etapas.Select(e => new { e.EtapaId, e.Nombre, e.Orden, e.EsFinal }));
    }

    /// <summary>Regla 3. Sin <c>cuentaId</c> devuelve el horario general.</summary>
    [HttpGet("configuracion/horario")]
    public async Task<IActionResult> ObtenerHorario([FromQuery] int? cuentaId, CancellationToken ct)
    {
        var tramos = await horarios.ObtenerTramosAsync(cuentaId, ct);

        return Ok(new HorarioVigente(
            cuentaId,
            await horarios.DescribirHorarioAsync(cuentaId, ct),
            [.. tramos.Select(t => new TramoHorario(t.DiaSemana, t.HoraInicio, t.HoraFin))]));
    }

    /// <summary>
    /// Reemplaza el horario completo. Es reemplazo y no edicion parcial porque una jornada se
    /// piensa como semana entera: mezclar tramos viejos con nuevos produce horarios que nadie
    /// configuro y que despues nadie entiende.
    /// </summary>
    [HttpPut("configuracion/horario")]
    [Authorize(Policy = Politicas.Estructura)]
    public async Task<IActionResult> GuardarHorario(
        [FromQuery] int? cuentaId,
        [FromBody] IReadOnlyList<TramoHorario> tramos,
        CancellationToken ct)
    {
        foreach (var tramo in tramos)
        {
            if (tramo.HoraFin <= tramo.HoraInicio)
            {
                return BadRequest(new
                {
                    motivo = $"El tramo del {tramo.DiaSemana} termina antes de empezar ({tramo.HoraInicio}-{tramo.HoraFin})."
                });
            }
        }

        var solapados = tramos
            .GroupBy(t => t.DiaSemana)
            .Where(g => g.OrderBy(t => t.HoraInicio)
                         .Zip(g.OrderBy(t => t.HoraInicio).Skip(1))
                         .Any(par => par.Second.HoraInicio < par.First.HoraFin))
            .Select(g => g.Key)
            .ToList();

        if (solapados.Count > 0)
            return BadRequest(new { motivo = $"Hay tramos superpuestos en: {string.Join(", ", solapados)}." });

        await horarios.ReemplazarTramosAsync(
            cuentaId,
            [.. tramos.Select(t => new HorarioAtencion
            {
                CuentaId = cuentaId,
                DiaSemana = t.DiaSemana,
                HoraInicio = t.HoraInicio,
                HoraFin = t.HoraFin
            })],
            User.AnalistaId(),
            ct);

        log.LogInformation("Horario de la cuenta {CuentaId} reemplazado con {Cantidad} tramo(s).",
            cuentaId, tramos.Count);

        return NoContent();
    }

    /// <summary>
    /// Parametros ajustables sin redeploy: las 2 horas, los 3 dias, los 90 dias y los topes de
    /// envio. Van con su descripcion, porque la clave sola no dice que regla gobiernan.
    /// </summary>
    [HttpGet("configuracion/reglas")]
    public async Task<IActionResult> ObtenerParametros(CancellationToken ct) =>
        Ok((await configuracion.ListarAsync(ct)).Select(p => new ParametroRegla(p.Clave, p.Valor, p.Descripcion)));

    /// <summary>
    /// Solo Sistemas (V23): el tope de envio por segundo es lo que protege la linea de otro
    /// bloqueo, y la retencion de CVs es un compromiso legal (Regla 17).
    /// </summary>
    [HttpPut("configuracion/reglas/{clave}")]
    [Authorize(Policy = Politicas.Estructura)]
    public async Task<IActionResult> GuardarParametro(
        string clave, [FromBody] string valor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return BadRequest(new { motivo = "El valor no puede quedar vacio." });

        var nuevo = valor.Trim();
        var actuales = await configuracion.ObtenerTodasAsync(ct);

        // Solo se cambian parametros que existen. Una clave mal escrita crearia una fila que ninguna
        // regla lee, y el cambio que se quiso hacer no pasaria nunca, sin un error a la vista.
        if (!actuales.TryGetValue(clave, out var actual))
            return NotFound(new { motivo = $"No existe el parametro '{clave}'." });

        if (ValidarTipo(actual, nuevo) is { } problema)
            return BadRequest(new { motivo = $"'{nuevo}' no sirve para '{clave}': {problema}" });

        await configuracion.EstablecerAsync(clave, nuevo, ct);

        log.LogInformation("El analista {AnalistaId} cambio {Clave} de {Anterior} a {Valor}.",
            User.AnalistaId(), clave, actual, nuevo);

        return NoContent();
    }

    /// <summary>
    /// El valor nuevo tiene que ser del mismo tipo que el actual. Las reglas los leen como numero o
    /// como si/no, y un "dos" las dejaria sin el parametro sin que nadie lo note. Nulo si sirve.
    /// </summary>
    private static string? ValidarTipo(string actual, string nuevo)
    {
        if (int.TryParse(actual, out _))
        {
            // Cero no es un plazo ni un tope: 0 dias de retencion purgaria todos los CVs en la
            // proxima vuelta del Worker, y 0 envios por segundo apagaria el bot.
            return int.TryParse(nuevo, out var numero) && numero > 0
                ? null
                : "tiene que ser un numero entero mayor que cero.";
        }

        if (bool.TryParse(actual, out _))
            return bool.TryParse(nuevo, out _) ? null : "tiene que ser true o false.";

        return null;
    }
}
