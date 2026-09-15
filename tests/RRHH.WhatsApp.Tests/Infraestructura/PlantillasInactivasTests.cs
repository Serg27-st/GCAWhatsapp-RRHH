using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// V8 y COR-16 (hallazgo M11): una plantilla solo se activa cuando Meta la aprobó. Enviar una
/// plantilla no aprobada es justo lo que provoca bloqueos, y el proyecto existe porque bloquearon
/// la línea.
/// </summary>
public class PlantillasInactivasTests
{
    [Fact]
    public void Una_plantilla_nueva_nace_inactiva()
    {
        // Antes el valor por defecto era true: la semilla lo corregía a mano, pero cualquier alta
        // desde código que omitiera Activa quedaba habilitada para enviar sin aprobación de Meta.
        var plantilla = new Plantilla { Clave = "nueva", NombreMeta = "rrhh_nueva", TextoAprobado = "Hola" };

        Assert.False(plantilla.Activa);
    }

    [Fact]
    public void Ninguna_plantilla_sembrada_esta_activa()
    {
        // Mira el modelo y no solo DatosSemilla: si una migración futura sembrara una activa, lo
        // que termina en la base es lo que dice el modelo.
        var opciones = new DbContextOptionsBuilder<RrhhDbContext>()
            .UseInMemoryDatabase($"plantillas-{Guid.NewGuid()}")
            .Options;

        using var db = new RrhhDbContext(opciones);

        // La semilla solo vive en el modelo de diseño, que es el que leen las migraciones.
        var modelo = db.GetService<IDesignTimeModel>().Model;
        var semilla = modelo.FindEntityType(typeof(Plantilla))!.GetSeedData().ToList();

        Assert.NotEmpty(semilla);
        Assert.All(semilla, fila => Assert.False((bool)fila[nameof(Plantilla.Activa)]!));
    }
}
