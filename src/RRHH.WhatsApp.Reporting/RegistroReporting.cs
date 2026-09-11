using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RRHH.WhatsApp.Reporting.Persistencia;

namespace RRHH.WhatsApp.Reporting;

public static class RegistroReporting
{
    /// <summary>
    /// Cablea el panel de gerencia con su propio contexto de solo lectura. Usa la misma base que
    /// el resto, pero por una conexion aparte: si algun dia el volumen justifica moverlo a una
    /// replica, alcanza con cambiar esta cadena.
    /// </summary>
    public static IServiceCollection AgregarReporting(
        this IServiceCollection servicios, IConfiguration configuracion)
    {
        var cadena = configuracion.GetConnectionString("RrhhWhatsAppReporting")
            ?? configuracion.GetConnectionString("RrhhWhatsApp")
            ?? throw new InvalidOperationException(
                "Falta la cadena de conexion para el panel de gerencia.");

        servicios.AddDbContext<ReportingDbContext>(opciones => opciones.UseSqlServer(cadena));
        servicios.AddScoped<IReportingReadModel, ReportingReadModel>();

        return servicios;
    }
}
