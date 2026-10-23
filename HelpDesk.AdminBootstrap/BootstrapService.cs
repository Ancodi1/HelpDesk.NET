using System.Data;
using HelpDesk.Api.Data;
using HelpDesk.Api.Models;
using HelpDesk.Api.Validation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.AdminBootstrap;

public enum BootstrapResult { Created, InvalidInput, Cancelled, AdminExists, EmailExists, WrongTarget }

public sealed class BootstrapService(IPasswordHasher<User> hasher)
{
    public const string Confirmation = "CONFIRMAR PRIMER ADMIN";

    public static string? Validate(string? name, string? email, string? password, string? repeatedPassword)
    {
        var error = UserValidation.Validate(name, email, nameof(UserRole.Admin), out _, out _)
            ?? PasswordValidation.Validate(password);
        if (error is not null) return error;
        return password == repeatedPassword ? null : "Las contraseñas no coinciden.";
    }

    public async Task<BootstrapResult> CreateAsync(HelpDeskDbContext db, BootstrapConnection target,
        string? databaseConfirmation, string? confirmation, string? name, string? email,
        string? password, string? repeatedPassword, CancellationToken cancellationToken = default)
    {
        if (databaseConfirmation != target.Database) return BootstrapResult.WrongTarget;
        if (confirmation != Confirmation) return BootstrapResult.Cancelled;
        if (Validate(name, email, password, repeatedPassword) is not null) return BootstrapResult.InvalidInput;

        var user = new User { Name = name!, Email = email!, Role = UserRole.Admin, CreatedAt = DateTime.UtcNow };
        // La operación costosa ocurre antes de bloquear: no hay interacción dentro de la transacción.
        user.PasswordHash = hasher.HashPassword(user, password!);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var actualDatabase = await db.Database.SqlQuery<string>($"SELECT current_database() AS \"Value\"")
            .SingleAsync(cancellationToken);
        var actualUser = await db.Database.SqlQuery<string>($"SELECT current_user::text AS \"Value\"")
            .SingleAsync(cancellationToken);
        if (actualDatabase != target.Database || actualUser != target.Username) return BootstrapResult.WrongTarget;

        // Autoconflictivo y contrario a ROW EXCLUSIVE de INSERT/UPDATE/DELETE, incluso de otros programas.
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE public.\"Users\" IN SHARE ROW EXCLUSIVE MODE", cancellationToken);
        if (await db.Users.AnyAsync(account => account.Role == UserRole.Admin, cancellationToken))
            return BootstrapResult.AdminExists;
        if (await db.Users.AnyAsync(account => account.Email == user.Email, cancellationToken))
            return BootstrapResult.EmailExists;

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return BootstrapResult.Created;
    }
}
