using System.Globalization;
using System.Security.Claims;
using HelpDesk.Api.Data;
using HelpDesk.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Api.Authorization;

public static class AccessPolicies
{
    public const string Admin = "Admin";
    public const string ChangeStatus = "ChangeStatus";

    public static AuthorizationPolicy AccountPolicy(params string[] roles)
    {
        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser()
            .AddRequirements(new CurrentAccountRequirement());
        if (roles.Length > 0) policy.RequireRole(roles);
        return policy.Build();
    }
}

public sealed class CurrentAccountRequirement : IAuthorizationRequirement { }

// Scoped: solo se completa tras comprobar la cuenta; nunca se toma el ID del cuerpo.
public sealed class CurrentAccount
{
    public int Id { get; internal set; }
    public UserRole Role { get; internal set; }
}

public sealed class CurrentAccountHandler(IServiceProvider services, CurrentAccount account)
    : AuthorizationHandler<CurrentAccountRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,
        CurrentAccountRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;
        var subjects = context.User.FindAll("sub").ToArray();
        var roles = context.User.FindAll("role").ToArray();
        if (subjects.Length != 1 || roles.Length != 1 ||
            !int.TryParse(subjects[0].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0 ||
            !Enum.IsDefined(typeof(UserRole), roles[0].Value))
            return;

        // Resolver el contexto después de autenticar evita acceder a datos con peticiones anónimas.
        var db = services.GetRequiredService<HelpDeskDbContext>();
        var stored = await db.Users.AsNoTracking().Where(user => user.Id == id)
            .Select(user => new { user.Id, user.Role }).SingleOrDefaultAsync();
        if (stored is null || stored.Role.ToString() != roles[0].Value) return;

        account.Id = stored.Id;
        account.Role = stored.Role;
        context.Succeed(requirement);
    }
}
