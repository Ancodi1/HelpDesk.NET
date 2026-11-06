using HelpDesk.Api.Data;
using HelpDesk.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HelpDesk.Api.Demo;

public sealed class DemoStartup(IServiceProvider services, IOptions<DemoSettings> options) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellation)
    {
        var settings = options.Value;
        settings.Validate();
        if (!settings.Enabled) return;
        await using var connection = await new DemoTarget(settings.ConnectionString, settings.InstanceId).OpenVerifiedAsync(cancellation);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        if ((await db.Database.GetPendingMigrationsAsync(cancellation)).Any())
            throw new InvalidOperationException("La demo requiere el esquema existente preparado; el arranque no ejecuta migraciones.");
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var users = await db.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync(cancellation);
        foreach (var (id, email, role, password) in new[]
        {
            (DemoAccounts.UserId, DemoAccounts.UserEmail, UserRole.User, settings.UserPassword),
            (DemoAccounts.TechnicianId, DemoAccounts.TechnicianEmail, UserRole.Technician, settings.TechnicianPassword),
            (DemoAccounts.AdminId, DemoAccounts.AdminEmail, UserRole.Admin, (string?)null)
        })
        {
            var user = users.SingleOrDefault(u => u.Id == id);
            if (user is null || user.Email != email || user.Role != role || string.IsNullOrEmpty(user.PasswordHash) ||
                (password is not null && hasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed))
                throw new InvalidOperationException("Las cuentas demo no coinciden con la configuración. Usa la consola antes de arrancar.");
        }
    }
    public Task StopAsync(CancellationToken cancellation) => Task.CompletedTask;
}
