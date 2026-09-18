using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Application.Reglas.Implementaciones;

/// <summary>
/// Regla 16 — reactivacion del hilo archivado (FUN-11, AL10).
/// <para>
/// Un hilo archivado que recibe un mensaje no puede quedarse archivado: ninguna bandeja lo muestra y
/// el postulante no recibiria respuesta de nadie. Si tiene un proceso vivo —tipicamente un reingreso
/// que el analista marco mientras estaba archivado—, vuelve con su analista; si no, el bot lo recibe
/// como a alguien que vuelve y le ofrece empezar de nuevo.
/// </para>
/// </summary>
public sealed class R16Reactivacion : IReglaNegocio
{
    public string Codigo => "R16";

    public string Descripcion => "Reactiva el hilo archivado cuando el postulante vuelve a escribir.";

    /// <summary>Antes que todas las del mensaje entrante: las demas asumen un hilo que no esta archivado.</summary>
    public int Prioridad => 12;

    public bool Aplica(ContextoRegla ctx) =>
        ctx.Disparador == TipoDisparador.MensajeEntrante
        && ctx.Conversacion is { Estado: EstadoConversacion.Archivada };

    public Task<ResultadoRegla> EvaluarAsync(ContextoRegla ctx, CancellationToken ct = default)
    {
        var vivos = ctx.PostulacionesDelPostulante
            .Where(p => p.Estado is EstadoPostulacion.EnProceso or EstadoPostulacion.Reingreso)
            .ToList();

        // A2: un proceso vivo es de alguien. La persona vuelve con su analista, sin menu; lo que
        // sigue (horario, repregunta) corre igual, porque el hilo ya tiene dueño.
        if (vivos.Count == 1)
        {
            return Task.FromResult(ResultadoRegla.Con(
                new ReactivarConversacion(EstadoConversacion.Activa),
                new TomarContextoDePostulacion(vivos[0].PostulacionId)));
        }

        // Con varios procesos vivos no se sabe por cual escribe: el hilo vuelve al bot y la Regla 6
        // le pregunta, en esta misma evaluacion (FUN-09).
        if (vivos.Count > 1)
        {
            return Task.FromResult(ResultadoRegla.Con(
                new ReactivarConversacion(EstadoConversacion.EnMenuBot),
                new LimpiarCuentaContexto("El postulante volvio a escribir con varios procesos vivos.")));
        }

        // Sin procesos vivos, vuelve a empezar: el contexto que tenia es de un proceso que ya termino.
        // Detiene porque el resto de las reglas asumiria ese contexto viejo.
        return Task.FromResult(ResultadoRegla.Detener(
            new ReactivarConversacion(EstadoConversacion.EnMenuBot),
            new LimpiarCuentaContexto("El postulante volvio a escribir despues del archivado."),
            new ReiniciarIntentosMenu(),
            new MostrarMenuEmpresas(EsReintento: false) { EsRegreso = true }));
    }
}
