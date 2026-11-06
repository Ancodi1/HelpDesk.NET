using HelpDesk.Api.Data;
using HelpDesk.Api.Demo;
using HelpDesk.Api.Models;
using HelpDesk.Api.Validation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Demo;

public sealed class DemoRestorer(IPasswordHasher<User> hasher)
{
    public async Task RestoreAsync(DemoTarget target, string? confirmation, bool initialize,
        string? userPassword = null, string? technicianPassword = null, string? adminPassword = null,
        CancellationToken cancellation = default)
    {
        // Confirmar antes de abrir una conexión, incluso en invocaciones no interactivas.
        if (confirmation != "RESTORE " + DemoTarget.Database + " " + target.InstanceId)
            throw new InvalidOperationException("Confirmación de destino incorrecta. No se ha abierto la conexión.");
        User[]? initialUsers = null;
        if (initialize)
        {
            if (new[] { userPassword, technicianPassword, adminPassword }.Any(p => PasswordValidation.Validate(p) is not null) ||
                new[] { userPassword, technicianPassword, adminPassword }.Distinct().Count() != 3)
                throw new InvalidOperationException("La inicialización requiere tres contraseñas distintas válidas, suministradas fuera de Git.");
            initialUsers = DemoDataset.Users();
            foreach (var (id, password) in new[] { (1, userPassword), (2, technicianPassword), (3, adminPassword) })
                initialUsers.Single(u => u.Id == id).PasswordHash = hasher.HashPassword(initialUsers.Single(u => u.Id == id), password!);
        }
        await using var connection = await target.OpenVerifiedAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        await DemoTarget.LockAsync(connection, transaction, cancellation);
        await using var db = new HelpDeskDbContext(new DbContextOptionsBuilder<HelpDeskDbContext>().UseNpgsql(connection).Options);
        await db.Database.UseTransactionAsync(transaction, cancellation);
        if ((await db.Database.GetPendingMigrationsAsync(cancellation)).Any())
            throw new InvalidOperationException("El esquema existente debe prepararse por separado. La consola no aplica migraciones.");
        // Nunca DROP DATABASE, TRUNCATE, cascadas, ni reinicio de secuencias/identidades.
        if (initialize)
        {
            if (await db.Users.AnyAsync(cancellation) || await db.Tickets.AnyAsync(cancellation) ||
                await db.TicketComments.AnyAsync(cancellation) || await db.TicketActivities.AnyAsync(cancellation))
                throw new InvalidOperationException("Inicialización rechazada: la base demo debe estar vacía.");
            db.Users.AddRange(initialUsers!);
            await db.SaveChangesAsync(cancellation);
            await db.Database.ExecuteSqlRawAsync("SELECT setval(pg_get_serial_sequence('public.\"Users\"', 'Id'), (SELECT max(\"Id\") FROM public.\"Users\"))", cancellation);
        }
        else
        {
            // Una restauración no borra ni cambia cuentas, hashes o roles, incluidas las normales.
            // Evita reutilizar IDs de identidades a las que se refieran JWT todavía vigentes.
            var users = await db.Users.AsNoTracking().ToListAsync(cancellation);
            foreach (var expected in DemoDataset.Users())
                if (!users.Any(u => u.Id == expected.Id && u.Email == expected.Email && u.Role == expected.Role))
                    throw new InvalidOperationException("Las identidades demo no coinciden. No se ha borrado contenido.");
            await db.TicketActivities.ExecuteDeleteAsync(cancellation);
            await db.TicketComments.ExecuteDeleteAsync(cancellation);
            await db.Tickets.ExecuteDeleteAsync(cancellation);
        }
        await DemoDataset.PopulateAsync(db, cancellation);
        await transaction.CommitAsync(cancellation);
    }
}
