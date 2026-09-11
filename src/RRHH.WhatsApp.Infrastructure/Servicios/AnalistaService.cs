using Microsoft.EntityFrameworkCore;
using RRHH.WhatsApp.Domain.Entidades;
using RRHH.WhatsApp.Domain.Enums;
using RRHH.WhatsApp.Domain.Interfaces;
using RRHH.WhatsApp.Infrastructure.Persistencia;

namespace RRHH.WhatsApp.Infrastructure.Servicios;

public sealed class AnalistaService(RrhhDbContext db) : IAnalistaService
{
    public async Task<IReadOnlyList<Analista>> ListarActivosAsync(CancellationToken ct = default) =>
        await db.Analistas
            .AsNoTracking()
            .Where(a => a.Activo)
            .OrderBy(a => a.Nombre)
            .ToListAsync(ct);


    public async Task<Analista> CrearAsync(
        string nombre, string email, RolAnalista rol, CancellationToken ct = default)
    {
        var correo = email.Trim().ToLowerInvariant();

        // El email es la identidad del analista y sera la llave cuando exista login: dos filas con
        // el mismo correo dejarian el sistema sin saber a cual de las dos entra la persona.
        if (await db.Analistas.AnyAsync(a => a.Email == correo, ct))
            throw new InvalidOperationException($"Ya existe un analista con el email '{correo}'.");

        var analista = new Analista
        {
            Nombre = nombre.Trim(),
            Email = correo,
            Rol = rol,
            Activo = true
        };

        db.Analistas.Add(analista);

        await db.SaveChangesAsync(ct);

        return analista;
    }
    public Task<Analista?> ObtenerPorIdAsync(int analistaId, CancellationToken ct = default) =>
        db.Analistas.AsNoTracking().FirstOrDefaultAsync(a => a.AnalistaId == analistaId, ct);
}
