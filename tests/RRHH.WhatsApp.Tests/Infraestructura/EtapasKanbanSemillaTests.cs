using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// T2.06 (COR-11): el desenlace de una columna del tablero es un dato de la etapa, no su nombre. Renombrar
/// «Descartado» en la administración no puede apagar el cierre de cortesía de la Regla 12.
/// </summary>
public class EtapasKanbanSemillaTests
{
    [Fact]
    public void Solo_las_columnas_finales_de_la_semilla_tienen_desenlace()
    {
        using var db = new RrhhDbContext(new DbContextOptionsBuilder<RrhhDbContext>()
            .UseInMemoryDatabase($"etapas-{Guid.NewGuid()}")
            .Options);

        var semilla = db.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(EtapaKanban))!
            .GetSeedData()
            .ToDictionary(fila => (int)fila[nameof(EtapaKanban.EtapaId)]!, fila => fila[nameof(EtapaKanban.EstadoResultante)]);

        Assert.Equal(EstadoPostulacion.Contratado, semilla[4]);
        Assert.Equal(EstadoPostulacion.Descartado, semilla[5]);
        Assert.All([1, 2, 3], etapa => Assert.Null(semilla[etapa]));
    }
}
