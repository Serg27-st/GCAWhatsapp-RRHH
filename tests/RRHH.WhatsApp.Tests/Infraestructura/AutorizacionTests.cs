using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using RRHH.WhatsApp.Api.Controllers;
using RRHH.WhatsApp.Api.Seguridad;

namespace RRHH.WhatsApp.Tests.Infraestructura;

/// <summary>
/// V23 — quien administra que. Se prueba sobre los atributos y no levantando la Api: lo que
/// importa es que cada escritura tenga un dueño declarado, y eso se lee del codigo.
/// </summary>
public class AutorizacionTests
{
    /// <summary>
    /// Escrituras que no llevan politica porque el permiso depende del recurso y se decide en el
    /// codigo. Agregar una aca es una decision: la prueba de abajo obliga a tomarla.
    /// </summary>
    private static readonly HashSet<string> VerificanEnElCodigo =
    [
        // Regla 4: FiltroAccesoConversacion, por conversacion.
        Nombre<ConversacionesController>(nameof(ConversacionesController.Responder)),
        Nombre<ConversacionesController>(nameof(ConversacionesController.Transferir)),
        Nombre<ConversacionesController>(nameof(ConversacionesController.Marcar)),

        // Regla 8: solo el analista destino.
        Nombre<TransferenciasController>(nameof(TransferenciasController.Responder)),

        // Regla 13 y V22: por la cuenta de la postulacion.
        Nombre<PostulacionesController>(nameof(PostulacionesController.MoverEtapa)),

        // V23: por la cuenta de la vacante.
        Nombre<VacantesController>(nameof(VacantesController.Crear)),
        Nombre<VacantesController>(nameof(VacantesController.Cerrar)),
        Nombre<VacantesController>(nameof(VacantesController.GuardarCampos)),

        // Regla 14: la propia, o Jefatura y Sistemas la de cualquiera.
        Nombre<AnalistasController>(nameof(AnalistasController.RegistrarAusencia)),
        Nombre<AnalistasController>(nameof(AnalistasController.CancelarAusencia)),

        // La propia contraseña, y exige la actual.
        Nombre<SesionController>(nameof(SesionController.CambiarContrasena))
    ];

    private static string Nombre<T>(string metodo) => $"{typeof(T).Name}.{metodo}";

    /// <summary>
    /// La misma idea que la politica de respaldo (V20): un endpoint nuevo que escribe tiene que
    /// declarar quien puede usarlo. Si no, esta prueba lo nombra.
    /// </summary>
    [Fact]
    public void Toda_escritura_tiene_dueno()
    {
        var escrituras = typeof(ConversacionesController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(EsEscritura)
                .Select(m => (Tipo: t, Metodo: m)))
            .ToList();

        // Sin esto, una reflexion que no encuentre nada pasaria la prueba sin proteger nada.
        Assert.Contains(escrituras, e => e.Tipo == typeof(CuentasController) && e.Metodo.Name == nameof(CuentasController.Crear));

        var sinDueno = escrituras
            .Where(e => !TieneDueno(e.Tipo, e.Metodo) && !VerificanEnElCodigo.Contains($"{e.Tipo.Name}.{e.Metodo.Name}"))
            .Select(e => $"{e.Tipo.Name}.{e.Metodo.Name}")
            .ToList();

        Assert.True(sinDueno.Count == 0, $"Escrituras sin dueño declarado: {string.Join(", ", sinDueno)}");
    }

    [Fact]
    public void Cada_endpoint_de_administracion_exige_su_politica()
    {
        (Type Controlador, string Accion, string Politica)[] esperados =
        [
            (typeof(CuentasController), nameof(CuentasController.Crear), Politicas.Estructura),
            (typeof(CuentasController), nameof(CuentasController.Asignar), Politicas.Jefatura),
            (typeof(CuentasController), nameof(CuentasController.Quitar), Politicas.Jefatura),
            (typeof(AnalistasController), nameof(AnalistasController.Crear), Politicas.Estructura),
            (typeof(ConfiguracionController), nameof(ConfiguracionController.GuardarHorario), Politicas.Estructura),
            (typeof(ConfiguracionController), nameof(ConfiguracionController.GuardarParametro), Politicas.Estructura),
            (typeof(ReportesController), nameof(ReportesController.Metricas), Politicas.Jefatura)
        ];

        var faltan = esperados
            .Where(e => !PoliticasDe(e.Controlador, e.Controlador.GetMethod(e.Accion)!).Contains(e.Politica))
            .Select(e => $"{e.Controlador.Name}.{e.Accion} → {e.Politica}")
            .ToList();

        Assert.True(faltan.Count == 0, $"Sin su politica: {string.Join(", ", faltan)}");
    }

    [Fact]
    public void Estructura_es_solo_de_Sistemas()
    {
        Assert.Equal([ClaimsAnalista.RolSistemas], RolesDe(Politicas.Estructura));
    }

    /// <summary>Sistemas conserva todo lo de Jefatura; un analista no entra en ninguna de las dos.</summary>
    [Fact]
    public void Jefatura_admite_a_Jefatura_y_a_Sistemas()
    {
        var roles = RolesDe(Politicas.Jefatura);

        Assert.Contains(ClaimsAnalista.RolJefatura, roles);
        Assert.Contains(ClaimsAnalista.RolSistemas, roles);
        Assert.DoesNotContain("Analista", roles);
    }

    private static bool EsEscritura(MethodInfo metodo) =>
        metodo.GetCustomAttributes<HttpMethodAttribute>(inherit: true)
            .Any(a => a.HttpMethods.Any(m => m != "GET" && m != "HEAD"));

    private static bool TieneDueno(Type controlador, MethodInfo metodo)
    {
        var atributos = metodo.GetCustomAttributes(inherit: true)
            .Concat(controlador.GetCustomAttributes(inherit: true))
            .ToList();

        return atributos.OfType<IAllowAnonymous>().Any()
            || atributos.OfType<IAuthorizeData>().Any(a =>
                !string.IsNullOrEmpty(a.Policy) || !string.IsNullOrEmpty(a.Roles));
    }

    private static IEnumerable<string?> PoliticasDe(Type controlador, MethodInfo metodo) =>
        metodo.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Concat(controlador.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
            .Select(a => a.Policy);

    private static string[] RolesDe(string politica)
    {
        var opciones = new AuthorizationOptions();
        Politicas.Registrar(opciones);

        return [.. opciones.GetPolicy(politica)!.Requirements
            .OfType<RolesAuthorizationRequirement>()
            .Single()
            .AllowedRoles];
    }
}
