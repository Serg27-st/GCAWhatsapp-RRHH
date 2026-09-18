using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Infrastructure.Servicios;

public sealed class CuentaService(RrhhDbContext db, IAlertaOperativaService alertas, TimeProvider reloj)
    : ICuentaService
{
    public async Task<IReadOnlyList<Cuenta>> ListarMenuAsync(CancellationToken ct = default) =>
        await db.Cuentas
            .AsNoTracking()
            .Where(c => c.Activo
                     && c.Asignaciones.Any(a => !a.EsBackup && a.Analista!.Activo)
                     && c.Vacantes.Any(v => v.Estado == EstadoHc.Abierta
                                         && v.UrlJobForms != null
                                         && v.UrlJobForms != ""))
            .OrderBy(c => c.Nombre)
            .ToListAsync(ct);


    public async Task<Hc?> BuscarVacantePorCodigoAsync(
        IReadOnlyCollection<string> candidatos, CancellationToken ct = default)
    {
        if (candidatos.Count == 0)
            return null;

        return await db.Hcs
            .AsNoTracking()
            .Where(h => h.CodigoAviso != null
                     && candidatos.Contains(h.CodigoAviso)
                     && h.Cuenta!.Activo)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Cuenta?> BuscarCuentaDeMenuPorNombreAsync(
        string textoNormalizado, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(textoNormalizado))
            return null;

        // La comparacion se hace en memoria sobre las ~20 cuentas del menu: normalizar el nombre
        // —mayusculas y sin tildes— no se puede traducir a SQL sin depender de la intercalacion.
        var cuentas = await ListarMenuAsync(ct);

        return cuentas.FirstOrDefault(c => CodigoAviso.Normalizar(c.Nombre) == textoNormalizado);
    }
    public Task<Hc?> ObtenerVacanteAsync(int hcId, CancellationToken ct = default) =>
        db.Hcs.AsNoTracking().FirstOrDefaultAsync(h => h.HcId == hcId, ct);

    public Task<Cuenta?> ObtenerPorIdAsync(int cuentaId, CancellationToken ct = default) =>
        db.Cuentas.AsNoTracking().FirstOrDefaultAsync(c => c.CuentaId == cuentaId, ct);

    public async Task<IReadOnlyList<Hc>> ListarVacantesAbiertasAsync(int cuentaId, CancellationToken ct = default) =>
        await db.Hcs
            .AsNoTracking()
            .Where(h => h.CuentaId == cuentaId && h.Estado == EstadoHc.Abierta)
            .OrderBy(h => h.Titulo)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Hc>> ListarVacantesDeCuentaAsync(
        int cuentaId, bool incluirCerradas, CancellationToken ct = default) =>
        await db.Hcs
            .AsNoTracking()
            .Where(h => h.CuentaId == cuentaId && (incluirCerradas || h.Estado == EstadoHc.Abierta))
            // Las abiertas primero: las cerradas solo se miran para reabrir una (FUN-20).
            .OrderBy(h => h.Estado)
            .ThenBy(h => h.Titulo)
            .ToListAsync(ct);

    public async Task<Hc> CrearVacanteAsync(
        int cuentaId, string titulo, string? urlJobForms, int analistaId, CancellationToken ct = default)
    {
        var existeCuenta = await db.Cuentas.AnyAsync(c => c.CuentaId == cuentaId && c.Activo, ct);

        if (!existeCuenta)
            throw new InvalidOperationException($"No existe una cuenta activa con id {cuentaId}.");

        var vacante = new Hc
        {
            CuentaId = cuentaId,
            Titulo = titulo,
            Estado = EstadoHc.Abierta,
            UrlJobForms = urlJobForms,
            CodigoAviso = await GenerarCodigoAvisoAsync(ct),
            FechaCreacion = reloj.GetUtcNow().UtcDateTime
        };

        db.Hcs.Add(vacante);

        // B2 (COR-18): la auditoria va despues del guardado. Antes, el HcId todavia era 0 y toda
        // vacante creada quedaba auditada contra la entidad «0», que no se puede rastrear.
        await db.SaveChangesAsync(ct);

        db.Auditorias.Add(Auditar(vacante.HcId, analistaId, "VacanteCreada", titulo));

        await db.SaveChangesAsync(ct);

        // COR-09 (V32): la vacante se crea igual —el analista suele no tener el enlace de Google a
        // mano—, pero queda fuera del menu hasta que alguien lo cargue. La alerta es lo que impide
        // que eso pase inadvertido y la vacante viva abierta sin recibir postulantes.
        if (string.IsNullOrWhiteSpace(urlJobForms))
        {
            await alertas.RegistrarAsync(TiposAlerta.VacanteSinFormulario, $"hc:{vacante.HcId}",
                $"La vacante {titulo} se creo sin formulario: no aparece en el menu del bot.", ct);
        }

        return vacante;
    }


    /// <summary>
    /// FUN-02 (A6): codigo corto y unico para el enlace del aviso. Se reintenta ante colision porque el
    /// codigo es aleatorio: con 31 simbolos y 6 posiciones el choque es raro, pero el indice unico lo
    /// rechazaria y el alta de la vacante fallaria por una razon que nadie podria corregir.
    /// </summary>
    private async Task<string> GenerarCodigoAvisoAsync(CancellationToken ct)
    {
        for (var intento = 0; intento < 10; intento++)
        {
            var codigo = CodigoAviso.Generar(Random.Shared);

            if (!await db.Hcs.AnyAsync(h => h.CodigoAviso == codigo, ct))
                return codigo;
        }

        throw new InvalidOperationException("No se pudo generar un codigo de aviso unico en 10 intentos.");
    }
    public async Task ActualizarVacanteAsync(
        int hcId, string? titulo, string? urlJobForms, string? codigoAviso, int analistaId,
        CancellationToken ct = default)
    {
        var vacante = await db.Hcs.FirstOrDefaultAsync(h => h.HcId == hcId, ct)
            ?? throw new InvalidOperationException($"No existe la vacante {hcId}.");

        var cambios = new List<string>();

        if (!string.IsNullOrWhiteSpace(titulo) && titulo.Trim() != vacante.Titulo)
        {
            vacante.Titulo = titulo.Trim();
            cambios.Add("titulo");
        }

        if (!string.IsNullOrWhiteSpace(urlJobForms))
        {
            var url = urlJobForms.Trim();

            // El enlace viaja en un mensaje de WhatsApp y por el se mandan el DNI y el CV: uno sin
            // cifrar dejaria esos datos al alcance de cualquiera en el camino.
            if (!Uri.TryCreate(url, UriKind.Absolute, out var direccion) || direccion.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("El enlace del formulario tiene que ser una URL https.");

            if (url != vacante.UrlJobForms)
            {
                vacante.UrlJobForms = url;
                cambios.Add("formulario");
            }
        }

        if (!string.IsNullOrWhiteSpace(codigoAviso))
        {
            var codigo = codigoAviso.Trim().ToUpperInvariant();

            // FUN-02: es lo que el postulante transcribe del aviso. Con un formato libre entrarian
            // codigos que se confunden al dictarlos o que no se pueden escribir en WhatsApp.
            if (!CodigoAviso.EsValido(codigo))
            {
                throw new InvalidOperationException(
                    $"El codigo '{codigo}' no sirve como codigo de aviso: entre 4 y 12 letras o numeros, sin signos.");
            }

            if (codigo != vacante.CodigoAviso)
            {
                if (await db.Hcs.AnyAsync(h => h.CodigoAviso == codigo && h.HcId != hcId, ct))
                    throw new InvalidOperationException($"El codigo '{codigo}' ya es de otra vacante.");

                vacante.CodigoAviso = codigo;
                cambios.Add("codigo");
            }
        }

        if (cambios.Count == 0)
            return;

        db.Auditorias.Add(Auditar(hcId, analistaId, "VacanteEditada", string.Join(", ", cambios)));

        await db.SaveChangesAsync(ct);

        // COR-09 (V32): si quedo sin formulario no aparece en el menu, y eso no puede pasar inadvertido.
        if (string.IsNullOrWhiteSpace(vacante.UrlJobForms))
        {
            await alertas.RegistrarAsync(TiposAlerta.VacanteSinFormulario, $"hc:{hcId}",
                $"La vacante {vacante.Titulo} no tiene formulario: no aparece en el menu del bot.", ct);
        }
    }

    public async Task ReabrirVacanteAsync(int hcId, int analistaId, CancellationToken ct = default)
    {
        var vacante = await db.Hcs.FirstOrDefaultAsync(h => h.HcId == hcId, ct)
            ?? throw new InvalidOperationException($"No existe la vacante {hcId}.");

        if (vacante.Estado == EstadoHc.Abierta)
            return;

        vacante.Estado = EstadoHc.Abierta;
        vacante.FechaCierre = null;

        db.Auditorias.Add(Auditar(hcId, analistaId, "VacanteReabierta", vacante.Titulo));

        await db.SaveChangesAsync(ct);
    }

    public async Task ActualizarCuentaAsync(
        int cuentaId, string? nombre, bool? activo, int analistaId, CancellationToken ct = default)
    {
        var cuenta = await db.Cuentas.FirstOrDefaultAsync(c => c.CuentaId == cuentaId, ct)
            ?? throw new InvalidOperationException($"No existe la cuenta {cuentaId}.");

        if (!string.IsNullOrWhiteSpace(nombre))
            cuenta.Nombre = nombre.Trim();

        var seDesactiva = activo is false && cuenta.Activo;

        if (activo is { } estado)
            cuenta.Activo = estado;

        db.Auditorias.Add(new Auditoria
        {
            EntidadTipo = nameof(Cuenta),
            EntidadId = cuentaId.ToString(),
            AnalistaId = analistaId,
            Accion = "CuentaEditada",
            Detalle = $"Nombre: {nombre ?? "sin cambios"}. Activa: {activo?.ToString() ?? "sin cambios"}.",
            Fecha = reloj.GetUtcNow().UtcDateTime
        });

        await db.SaveChangesAsync(ct);

        if (!seDesactiva)
            return;

        // Desactivarla la saca del menu, pero lo que ya esta en curso sigue con su analista: moverlo
        // seria decidir por el, y cerrarlo, dejar postulantes sin respuesta. Queda la alerta (ARQ-09).
        var abiertas = await db.Conversaciones.CountAsync(
            c => c.CuentaContextoId == cuentaId
              && c.Estado != EstadoConversacion.Archivada
              && c.Estado != EstadoConversacion.Cerrada, ct);

        if (abiertas > 0)
        {
            await alertas.RegistrarAsync(TiposAlerta.CuentaDesactivadaConConversaciones, $"cuenta:{cuentaId}",
                $"La cuenta {cuenta.Nombre} se desactivo con {abiertas} conversacion(es) en curso.", ct);
        }
    }

    public async Task CerrarVacanteAsync(int hcId, int analistaId, CancellationToken ct = default)
    {
        var vacante = await db.Hcs.FirstOrDefaultAsync(h => h.HcId == hcId, ct)
            ?? throw new InvalidOperationException($"No existe la vacante {hcId}.");

        if (vacante.Estado == EstadoHc.Cerrada)
            return;

        vacante.Estado = EstadoHc.Cerrada;
        vacante.FechaCierre = reloj.GetUtcNow().UtcDateTime;

        // Regla 20: desde aca el bot deja de ofrecerla y el formulario deja de aceptar envios.
        // El formulario de Google, en cambio, hay que desactivarlo a mano (ver docs/decisiones.md).
        db.Auditorias.Add(Auditar(hcId, analistaId, "VacanteCerrada", vacante.Titulo));

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<(Cuenta Cuenta, bool EsBackup, int VacantesAbiertas)>> ListarDeAnalistaAsync(
        int analistaId, CancellationToken ct = default)
    {
        var filas = await db.AnalistaCuentas
            .AsNoTracking()
            .Include(ac => ac.Cuenta)
            .Where(ac => ac.AnalistaId == analistaId && ac.Cuenta!.Activo)
            .Select(ac => new
            {
                ac.Cuenta,
                ac.EsBackup,
                Abiertas = ac.Cuenta!.Vacantes.Count(v => v.Estado == EstadoHc.Abierta)
            })
            .OrderBy(f => f.Cuenta!.Nombre)
            .ToListAsync(ct);

        return [.. filas.Select(f => (f.Cuenta!, f.EsBackup, f.Abiertas))];
    }

    public async Task<NivelAcceso> ObtenerAccesoAsync(int cuentaId, int analistaId, CancellationToken ct = default)
    {
        // El respaldo es de la cuenta, no una visita: la Regla 2 le pasa las conversaciones y la
        // 14 las nuevas mientras el titular esta ausente, y tiene que poder seguir el tablero.
        if (await db.AnalistaCuentas.AnyAsync(ac => ac.CuentaId == cuentaId && ac.AnalistaId == analistaId, ct))
            return NivelAcceso.Total;

        var esSistemas = await db.Analistas.AnyAsync(
            a => a.AnalistaId == analistaId && a.Rol == RolAnalista.Sistemas, ct);

        return esSistemas ? NivelAcceso.Lectura : NivelAcceso.Ninguno;
    }


    public async Task<Cuenta> CrearAsync(string nombre, CancellationToken ct = default)
    {
        var limpio = nombre.Trim();

        if (await db.Cuentas.AnyAsync(c => c.Nombre == limpio, ct))
            throw new InvalidOperationException($"Ya existe una cuenta llamada '{limpio}'.");

        var cuenta = new Cuenta { Nombre = limpio, Activo = true };

        db.Cuentas.Add(cuenta);

        await db.SaveChangesAsync(ct);

        return cuenta;
    }

    public async Task<IReadOnlyList<Cuenta>> ListarTodasAsync(CancellationToken ct = default) =>
        await db.Cuentas.AsNoTracking().OrderBy(c => c.Nombre).ToListAsync(ct);

    public async Task<IReadOnlyList<(Cuenta Cuenta, Analista? Titular, Analista? Respaldo, int VacantesAbiertas)>>
        ListarConDotacionAsync(CancellationToken ct = default)
    {
        var filas = await db.Cuentas
            .AsNoTracking()
            .OrderBy(c => c.Nombre)
            .Select(c => new
            {
                Cuenta = c,
                Titular = c.Asignaciones.Where(a => !a.EsBackup).Select(a => a.Analista).FirstOrDefault(),
                Respaldo = c.Asignaciones.Where(a => a.EsBackup).Select(a => a.Analista).FirstOrDefault(),
                Abiertas = c.Vacantes.Count(v => v.Estado == EstadoHc.Abierta)
            })
            .ToListAsync(ct);

        return [.. filas.Select(f => (f.Cuenta, f.Titular, f.Respaldo, f.Abiertas))];
    }

    public async Task AsignarAnalistaAsync(
        int cuentaId, int analistaId, bool esBackup, CancellationToken ct = default)
    {
        if (!await db.Cuentas.AnyAsync(c => c.CuentaId == cuentaId, ct))
            throw new InvalidOperationException($"No existe la cuenta {cuentaId}.");

        var analista = await db.Analistas
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.AnalistaId == analistaId && a.Activo, ct)
            ?? throw new InvalidOperationException($"No existe un analista activo con id {analistaId}.");

        // V23: Jefatura y Sistemas no atienden conversaciones. Como titular o respaldo, la Regla 1 o la
        // 2 les pasarian conversaciones que nadie va a responder.
        if (analista.Rol != RolAnalista.Analista)
        {
            throw new InvalidOperationException(
                $"{analista.Nombre} tiene rol {analista.Rol}: solo un analista que atiende conversaciones puede cubrir una cuenta.");
        }

        var asignaciones = await db.AnalistaCuentas.Where(ac => ac.CuentaId == cuentaId).ToListAsync(ct);

        // Que el titular y el respaldo sean personas distintas lo garantiza el indice unico
        // (AnalistaId, CuentaId): un analista tiene una sola fila por cuenta, con un solo rol.
        var existente = asignaciones.FirstOrDefault(ac => ac.AnalistaId == analistaId);

        // Regla 1: la cuenta tiene un titular, y la 2 un respaldo. Se reemplaza a quien ocupa el lugar
        // en vez de acumular: con dos titulares, el enrutamiento elegiria uno de forma arbitraria.
        var ocupante = asignaciones.FirstOrDefault(ac => ac.EsBackup == esBackup && ac.AnalistaId != analistaId);

        if (ocupante is not null)
        {
            db.AnalistaCuentas.Remove(ocupante);

            // El respaldo que pasa a titular, o al reves: el ocupante se saca primero y aparte. EF no
            // sabe que EsBackup es parte del filtro del indice unico, y podria mandar el cambio de rol
            // antes que el borrado; por un instante habria dos titulares y SQL Server lo rechazaria.
            if (existente is not null)
                await db.SaveChangesAsync(ct);
        }

        if (existente is not null)
        {
            existente.EsBackup = esBackup;
        }
        else
        {
            db.AnalistaCuentas.Add(new AnalistaCuenta
            {
                CuentaId = cuentaId,
                AnalistaId = analistaId,
                EsBackup = esBackup
            });
        }

        db.Auditorias.Add(new Auditoria
        {
            EntidadTipo = nameof(Cuenta),
            EntidadId = cuentaId.ToString(),
            Detalle = $"Analista {analistaId}.",
            Accion = esBackup ? "RespaldoAsignado" : "TitularAsignado",
            Fecha = reloj.GetUtcNow().UtcDateTime
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task QuitarAnalistaAsync(int cuentaId, int analistaId, CancellationToken ct = default)
    {
        var asignacion = await db.AnalistaCuentas
            .FirstOrDefaultAsync(ac => ac.CuentaId == cuentaId && ac.AnalistaId == analistaId, ct);

        if (asignacion is null)
            return;

        db.AnalistaCuentas.Remove(asignacion);

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<HcCampoOpcional>> ListarCamposOpcionalesAsync(
        int hcId, CancellationToken ct = default) =>
        await db.HcCamposOpcionales
            .AsNoTracking()
            .Where(c => c.HcId == hcId)
            .OrderBy(c => c.CampoId)
            .ToListAsync(ct);

    public async Task ReemplazarCamposOpcionalesAsync(
        int hcId, IReadOnlyList<HcCampoOpcional> campos, int analistaId, CancellationToken ct = default)
    {
        var existeVacante = await db.Hcs.AnyAsync(h => h.HcId == hcId, ct);

        if (!existeVacante)
            throw new InvalidOperationException($"No existe la vacante {hcId}.");

        foreach (var campo in campos.Where(c => string.IsNullOrWhiteSpace(c.NombreCampo)))
            throw new InvalidOperationException("Un campo opcional necesita nombre.");

        // Dos campos con el mismo nombre dejarian el formulario con dos preguntas identicas y sin
        // forma de distinguir las respuestas al leer el DatosJson.
        var repetido = campos
            .GroupBy(c => c.NombreCampo.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        if (repetido is not null)
            throw new InvalidOperationException($"El campo '{repetido.Key}' esta repetido.");

        var actuales = await db.HcCamposOpcionales.Where(c => c.HcId == hcId).ToListAsync(ct);

        db.HcCamposOpcionales.RemoveRange(actuales);

        foreach (var campo in campos)
        {
            db.HcCamposOpcionales.Add(new HcCampoOpcional
            {
                HcId = hcId,
                NombreCampo = campo.NombreCampo.Trim(),
                Tipo = campo.Tipo,
                Activo = campo.Activo
            });
        }

        db.Auditorias.Add(Auditar(hcId, analistaId, "CamposOpcionalesActualizados",
            $"{campos.Count} campo(s) configurados."));

        await db.SaveChangesAsync(ct);
    }
    private Auditoria Auditar(int hcId, int analistaId, string accion, string? detalle) =>
        new()
        {
            EntidadTipo = nameof(Hc),
            EntidadId = hcId.ToString(),
            AnalistaId = analistaId,
            Accion = accion,
            Detalle = detalle,
            Fecha = reloj.GetUtcNow().UtcDateTime
        };
}
