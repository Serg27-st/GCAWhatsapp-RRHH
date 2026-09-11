using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;
using RRHH.WhatsApp.Infrastructure.Servicios;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// Campos opcionales por vacante (Seccion 9.2). Son lo que el analista activa al abrir el HC y lo
/// que el JobForms pregunta ademas de los campos fijos.
/// </summary>
public class CamposOpcionalesTests : IDisposable
{
    private const int AnalistaId = 10;

    private readonly RrhhDbContext _db;
    private readonly ICuentaService _cuentas;

    public CamposOpcionalesTests()
    {
        _db = new RrhhDbContext(new DbContextOptionsBuilder<RrhhDbContext>()
            .UseInMemoryDatabase($"campos-{Guid.NewGuid()}")
            .Options);

        _db.Database.EnsureCreated();

        _db.Cuentas.Add(new Cuenta { CuentaId = 7, Nombre = "Alicorp", Activo = true });
        _db.Hcs.Add(new Hc
        {
            HcId = 1,
            CuentaId = 7,
            Titulo = "Operario de produccion",
            Estado = EstadoHc.Abierta,
            FechaCreacion = DateTime.UtcNow
        });

        _db.SaveChanges();

        _cuentas = new CuentaService(_db);
    }

    private static HcCampoOpcional Campo(string nombre, TipoCampoOpcional tipo = TipoCampoOpcional.Texto,
        bool activo = true) =>
        new() { HcId = 1, NombreCampo = nombre, Tipo = tipo, Activo = activo };

    [Fact]
    public async Task Una_vacante_nueva_no_tiene_campos_opcionales()
    {
        Assert.Empty(await _cuentas.ListarCamposOpcionalesAsync(1));
    }

    [Fact]
    public async Task Se_guardan_los_campos_configurados_para_la_vacante()
    {
        await _cuentas.ReemplazarCamposOpcionalesAsync(1, [
            Campo("Anos de experiencia", TipoCampoOpcional.Numero),
            Campo("Disponibilidad inmediata", TipoCampoOpcional.Booleano)
        ], AnalistaId);

        var campos = await _cuentas.ListarCamposOpcionalesAsync(1);

        Assert.Equal(2, campos.Count);
        Assert.Equal(TipoCampoOpcional.Numero, campos[0].Tipo);
        Assert.Equal("Disponibilidad inmediata", campos[1].NombreCampo);
    }

    [Fact]
    public async Task Guardar_reemplaza_el_catalogo_completo_y_no_acumula()
    {
        // El analista los define como un conjunto: ir agregando de a uno dejaria el formulario
        // en estados intermedios que nadie configuro.
        await _cuentas.ReemplazarCamposOpcionalesAsync(1, [Campo("Viejo")], AnalistaId);
        await _cuentas.ReemplazarCamposOpcionalesAsync(1, [Campo("Nuevo")], AnalistaId);

        var campos = await _cuentas.ListarCamposOpcionalesAsync(1);

        Assert.Equal("Nuevo", Assert.Single(campos).NombreCampo);
    }

    [Fact]
    public async Task Un_campo_repetido_se_rechaza()
    {
        // Dos preguntas identicas dejarian el DatosJson sin forma de distinguir las respuestas.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _cuentas.ReemplazarCamposOpcionalesAsync(1, [
                Campo("Licencia de conducir"),
                Campo("licencia de conducir")
            ], AnalistaId));

        Assert.Contains("repetido", ex.Message);
    }

    [Fact]
    public async Task Un_campo_sin_nombre_se_rechaza()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _cuentas.ReemplazarCamposOpcionalesAsync(1, [Campo("  ")], AnalistaId));
    }

    [Fact]
    public async Task No_se_pueden_configurar_campos_de_una_vacante_inexistente()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _cuentas.ReemplazarCamposOpcionalesAsync(999, [Campo("Algo")], AnalistaId));
    }

    [Fact]
    public async Task Un_campo_desactivado_se_conserva_pero_queda_marcado()
    {
        // Desactivar no es borrar: la respuesta historica de quien ya lo contesto sigue teniendo
        // sentido, y el analista puede volver a activarlo sin recordar como se llamaba.
        await _cuentas.ReemplazarCamposOpcionalesAsync(1, [
            Campo("Movilidad propia", activo: false)
        ], AnalistaId);

        var campo = Assert.Single(await _cuentas.ListarCamposOpcionalesAsync(1));

        Assert.False(campo.Activo);
    }

    [Fact]
    public async Task El_cambio_queda_en_la_auditoria()
    {
        await _cuentas.ReemplazarCamposOpcionalesAsync(1, [Campo("Turno rotativo")], AnalistaId);

        var registrado = await _db.Auditorias.AnyAsync(
            a => a.Accion == "CamposOpcionalesActualizados" && a.AnalistaId == AnalistaId);

        Assert.True(registrado);
    }

    public void Dispose() => _db.Dispose();
}
