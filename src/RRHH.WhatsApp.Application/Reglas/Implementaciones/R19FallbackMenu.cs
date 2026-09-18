using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas.Implementaciones;

/// <summary>
/// Regla 19 — Fallback de menu no reconocido.
/// <para>
/// Si el postulante escribe texto libre en vez de usar los botones, el bot reintenta mostrando el
/// menu las veces que diga <see cref="ClavesConfiguracion.MenuReintentosPermitidos"/>. Agotadas,
/// la conversacion pasa a la bandeja general de pendientes por clasificar, visible para todos los
/// analistas, para que cualquiera la tome (FUN-01).
/// </para>
/// <para>
/// El contador vive en la conversacion (COR-06). Antes se calculaba como «todos los entrantes menos
/// uno», asi que un postulante con historia al que se le limpiaba el contexto entraba ya por encima
/// del umbral y su siguiente mensaje iba directo a la bandeja general sin ver el menu (AL2).
/// </para>
/// </summary>
public sealed class R19FallbackMenu : IReglaNegocio
{
    public string Codigo => "R19";

    public string Descripcion => "Reintenta el menu y luego deriva a la bandeja general de pendientes por clasificar.";

    /// <summary>Antes que la asignacion: mientras no haya cuenta identificada no hay a quien asignar.</summary>
    public int Prioridad => 15;

    public bool Aplica(ContextoRegla ctx) =>
        ctx.Disparador == TipoDisparador.MensajeEntrante
        // Solo mientras el bot esta atendiendo: en «Sin clasificar» el hilo ya espera a una persona,
        // y volver a mostrarle el menu lo sacaria de esa cola (B6, V30).
        && ctx.Conversacion is { Estado: EstadoConversacion.EnMenuBot }
        && ctx.Cuenta is null
        // FUN-02: si el mensaje trajo un codigo de aviso o el nombre de una empresa, el postulante si
        // eligio, aunque la cuenta no se haya podido cargar. Mostrarle el menu seria ignorarlo.
        && ctx.OrigenEleccion == OrigenEleccion.Ninguna
        // FUN-08 (A2): con un unico proceso vivo tampoco hay nada que preguntar; lo retoma la R01.
        && ctx.CuentasVivas.Count != 1;

    public Task<ResultadoRegla> EvaluarAsync(ContextoRegla ctx, CancellationToken ct = default)
    {
        // FUN-03: pedir otra pagina del menu no es fallar en elegir; el contador no se mueve.
        if (ctx.PaginaMenu > 0)
            return Task.FromResult(ResultadoRegla.Con(new MostrarMenuEmpresas(EsReintento: false, ctx.PaginaMenu)));

        var intentos = ctx.IntentosMenuFallidos;
        var permitidos = ctx.ConfigInt(ClavesConfiguracion.MenuReintentosPermitidos, 1);

        // El primer mensaje del hilo no es un fallo del postulante: todavia no vio ningun menu.
        if (intentos < permitidos + 1)
        {
            return Task.FromResult(ResultadoRegla.Con(
                new RegistrarIntentoMenu(TextoNoReconocido: intentos > 0),
                new MostrarMenuEmpresas(EsReintento: intentos > 0)));
        }

        // Agotados los reintentos, la conversacion no se pierde: queda visible para todo el equipo,
        // con la fecha desde la que espera (P3).
        return Task.FromResult(ResultadoRegla.Con(
            new DerivarAPendientes(
                $"El postulante no eligio una opcion valida tras {intentos} intentos.")));
    }
}
