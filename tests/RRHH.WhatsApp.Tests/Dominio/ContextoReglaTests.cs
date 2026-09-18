using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Reglas;

namespace RRHH.WhatsApp.Tests.Dominio;

/// <summary>
/// ARQ-07: los derivados del contexto resumen las postulaciones del postulante para que cada regla
/// no repita la misma cuenta. Se prueban aca porque R9, R16 y la desambiguacion (A3) leen los mismos
/// derivados: si uno se equivoca, se equivocan todas a la vez.
/// </summary>
public class ContextoReglaTests
{
    private static readonly DateTime Ahora = new(2026, 9, 14, 15, 0, 0, DateTimeKind.Utc);

    private static PostulacionVigente Postulacion(int id, int cuentaId, EstadoPostulacion estado) =>
        new(id, cuentaId, $"Cuenta {cuentaId}", HcId: id * 10, Vacante: $"Vacante {id}", estado,
            AnalistaAsignadoId: 10, FechaUltimaActividad: Ahora.AddDays(-1));

    private static ContextoRegla Contexto(
        IReadOnlyList<PostulacionVigente>? postulaciones = null,
        DateTime? actividadAnterior = null) => new()
    {
        Disparador = TipoDisparador.MensajeEntrante,
        AhoraUtc = Ahora,
        Configuracion = new Dictionary<string, string>(),
        PostulacionesDelPostulante = postulaciones ?? [],
        FechaActividadAnterior = actividadAnterior
    };

    [Fact]
    public void Sin_postulaciones_no_hay_proceso_vivo()
    {
        var ctx = Contexto();

        Assert.False(ctx.TieneProcesoVivo);
        Assert.Empty(ctx.CuentasVivas);
    }

    /// <summary>A2: el reingreso cuenta como proceso vivo igual que uno en curso.</summary>
    [Theory]
    [InlineData(EstadoPostulacion.EnProceso)]
    [InlineData(EstadoPostulacion.Reingreso)]
    public void En_proceso_y_reingreso_son_procesos_vivos(EstadoPostulacion estado)
    {
        Assert.True(Contexto([Postulacion(1, 5, estado)]).TieneProcesoVivo);
    }

    /// <summary>Un proceso ya decidido o archivado no retiene al postulante en la cuenta.</summary>
    [Theory]
    [InlineData(EstadoPostulacion.Contratado)]
    [InlineData(EstadoPostulacion.Descartado)]
    [InlineData(EstadoPostulacion.Archivada)]
    public void Los_procesos_cerrados_no_son_vivos(EstadoPostulacion estado)
    {
        var ctx = Contexto([Postulacion(1, 5, estado)]);

        Assert.False(ctx.TieneProcesoVivo);
        Assert.Empty(ctx.CuentasVivas);
    }

    /// <summary>
    /// A3: dos vacantes vivas en la misma cuenta son una sola opcion para el menu de procesos; lo que
    /// se desambigua es la cuenta, no la vacante.
    /// </summary>
    [Fact]
    public void Las_cuentas_vivas_no_se_repiten_y_excluyen_las_cerradas()
    {
        var ctx = Contexto(
        [
            Postulacion(1, 5, EstadoPostulacion.EnProceso),
            Postulacion(2, 7, EstadoPostulacion.Descartado),
            Postulacion(3, 5, EstadoPostulacion.Reingreso),
            Postulacion(4, 9, EstadoPostulacion.EnProceso)
        ]);

        Assert.Equal([5, 9], ctx.CuentasVivas);
    }

    /// <summary>C1: la inactividad se mide contra la instantanea tomada antes de registrar el mensaje.</summary>
    [Fact]
    public void Los_dias_desde_la_actividad_anterior_salen_de_la_instantanea()
    {
        Assert.Equal(3.5, Contexto(actividadAnterior: Ahora.AddDays(-3.5)).DiasDesdeActividadAnterior);
    }

    [Fact]
    public void Sin_actividad_anterior_no_hay_dias()
    {
        Assert.Null(Contexto().DiasDesdeActividadAnterior);
    }

    /// <summary>FUN-03: la Regla 19 distingue «pidio otra pagina» de «no eligio nada» por la pagina en cero.</summary>
    [Fact]
    public void Por_defecto_nadie_eligio_nada_ni_pidio_otra_pagina()
    {
        var ctx = Contexto();

        Assert.Equal(OrigenEleccion.Ninguna, ctx.OrigenEleccion);
        Assert.Equal(0, ctx.PaginaMenu);
        Assert.Null(ctx.PostulacionElegidaId);
        Assert.Null(ctx.TransferenciaPendiente);
        Assert.False(ctx.EnviarCierreSolicitado);
    }
}
