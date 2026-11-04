using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HelpDesk.Api.Data;
using HelpDesk.Api.Models;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HelpDesk.Tests.Integration;

public partial class TicketEndpointsTests
{
    private async Task RequireNoAdminsForLastAdminAsync()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        Assert.False(await db.Users.AnyAsync(u => u.Role == UserRole.Admin),
            "Estos casos requieren una base exclusiva de pruebas sin Admin previo. No se modifican cuentas ajenas para preparar el caso.");
    }

    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.Technician)]
    public async Task LastAdmin_DemotionReturnsConflictAndPreservesAllFieldsAndSession(UserRole role)
    {
        await RequireNoAdminsForLastAdminAsync();
        var actor = await SeedHttpUserAsync(UserRole.Admin);
        var ticket = await CreateTicketAsync(createdByUserId: actor.Id);
        using var client = CreateAuthenticatedClient(actor);
        using var response = await client.PutAsJsonAsync($"/api/users/{actor.Id}", new
        {
            name = "Must not be saved", email = $"changed-{actor.Email}", role = role.ToString()
        });
        await AssertLastAdminConflictAsync(response);
        await AssertUserStoredAsync(new(actor.Id, actor.Name, actor.Email, actor.Role, actor.CreatedAt));
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            Assert.Equal(1, await db.Users.CountAsync(u => u.Role == UserRole.Admin));
            Assert.Null((await db.Users.AsNoTracking().SingleAsync(u => u.Id == actor.Id)).PasswordHash);
            AssertTicketEqual(ticket, await db.Tickets.AsNoTracking().SingleAsync(t => t.Id == ticket.Id));
        }
        using var directory = await client.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.OK, directory.StatusCode);
        using var detail = await client.GetAsync($"/api/tickets/{ticket.Id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        AssertTicketEqual(ticket, (await detail.Content.ReadFromJsonAsync<Ticket>(JsonOptions))!);
        // Rechazar una degradación no impide editar nombre/correo manteniendo Admin.
        using var edit = await client.PutAsJsonAsync($"/api/users/{actor.Id}", new
        {
            name = "Allowed name", actor.Email, role = "Admin"
        });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Equal(UserRole.Admin, (await ReadUserResponseAsync(edit)).Role);
    }

    [Theory]
    [InlineData(UserRole.User, false)]
    [InlineData(UserRole.Technician, false)]
    [InlineData(UserRole.User, true)]
    [InlineData(UserRole.Technician, true)]
    public async Task LastAdmin_AnotherAdminAllowsSelfOrOtherDemotion(UserRole role, bool self)
    {
        await RequireNoAdminsForLastAdminAsync();
        var actor = await SeedHttpUserAsync(UserRole.Admin);
        var other = await SeedHttpUserAsync(UserRole.Admin);
        var target = self ? actor : other;
        using var client = CreateAuthenticatedClient(actor);
        using var response = await client.PutAsJsonAsync($"/api/users/{target.Id}", new
        {
            target.Name, target.Email, role = role.ToString()
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ReadUserResponseAsync(response);
        Assert.Equal(role, updated.Role);
        await AssertUserStoredAsync(updated);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(u => u.Role == UserRole.Admin));
        using var directory = await client.GetAsync("/api/users");
        Assert.Equal(self ? HttpStatusCode.Forbidden : HttpStatusCode.OK, directory.StatusCode);
    }

    [Fact]
    public async Task LastAdmin_AssignmentRestrictionStillAppliesWithAnotherAdmin()
    {
        await RequireNoAdminsForLastAdminAsync();
        var actor = await SeedHttpUserAsync(UserRole.Admin);
        var target = await SeedHttpUserAsync(UserRole.Admin);
        var ticket = await CreateTicketAsync(assignedToUserId: target.Id);
        using var client = CreateAuthenticatedClient(actor);
        using var response = await client.PutAsJsonAsync($"/api/users/{target.Id}", new
        {
            name = "Must not change", target.Email, role = "User"
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.TryGetProperty("code", out _));
        await AssertUserStoredAsync(new(target.Id, target.Name, target.Email, target.Role, target.CreatedAt));
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        AssertTicketEqual(ticket, await db.Tickets.AsNoTracking().SingleAsync(t => t.Id == ticket.Id));
    }

    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.Technician)]
    public async Task LastAdmin_NonAdminStillCannotChangeRoles(UserRole role)
    {
        await RequireNoAdminsForLastAdminAsync();
        var admin = await SeedHttpUserAsync(UserRole.Admin);
        var actor = await SeedHttpUserAsync(role);
        using var client = CreateAuthenticatedClient(actor);
        using var response = await client.PutAsJsonAsync($"/api/users/{admin.Id}", new
        {
            admin.Name, admin.Email, role = "Technician"
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
        await AssertUserStoredAsync(new(admin.Id, admin.Name, admin.Email, admin.Role, admin.CreatedAt));
    }

    [Fact]
    public async Task LastAdmin_ExistingNotFoundValidationAndDuplicateContractsRemain()
    {
        await RequireNoAdminsForLastAdminAsync();
        var actor = await SeedHttpUserAsync(UserRole.Admin);
        var other = await SeedHttpUserAsync();
        using var client = CreateAuthenticatedClient(actor);
        using var absent = await client.PutAsJsonAsync("/api/users/-1", new { actor.Name, actor.Email, role = "User" });
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        using var invalid = await client.PutAsJsonAsync($"/api/users/{actor.Id}", new { name = "", actor.Email, role = "User" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var duplicate = await client.PutAsJsonAsync($"/api/users/{actor.Id}", new { actor.Name, other.Email, role = "Admin" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        await AssertUserStoredAsync(new(actor.Id, actor.Name, actor.Email, actor.Role, actor.CreatedAt));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LastAdmin_ConcurrentDemotionsSerializeAndLeaveOneAdmin(bool reverse)
    {
        await RequireNoAdminsForLastAdminAsync();
        var a = await SeedHttpUserAsync(UserRole.Admin);
        var b = await SeedHttpUserAsync(UserRole.Admin);
        var firstActor = reverse ? b : a;
        var secondActor = reverse ? a : b;
        var ticket = await CreateTicketAsync(createdByUserId: a.Id);
        var pause = new PauseRoleOrAssignmentSave(firstActor.Id, -1, false);
        var probe = new UserTableLockProbe();
        using var firstFactory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<HelpDeskDbContext>(options => options.AddInterceptors(pause))));
        using var secondFactory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<HelpDeskDbContext>(options => options.AddInterceptors(probe))));
        using var firstClient = CreateAuthenticatedClient(firstActor, firstFactory);
        using var secondClient = CreateAuthenticatedClient(secondActor, secondFactory);
        var first = firstClient.PutAsJsonAsync($"/api/users/{firstActor.Id}", new
        {
            firstActor.Name, firstActor.Email, role = "Technician"
        });
        Task<HttpResponseMessage>? second = null;
        try
        {
            var holder = await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            second = secondClient.PutAsJsonAsync($"/api/users/{secondActor.Id}", new
            {
                secondActor.Name, secondActor.Email, role = "User"
            });
            var waiter = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await AssertBlockedByAsync(waiter, holder);
            Assert.False(second.IsCompleted);
            pause.Resume.TrySetResult();
            using var firstResponse = await first.WaitAsync(TimeSpan.FromSeconds(15));
            using var secondResponse = await second.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            await AssertLastAdminConflictAsync(secondResponse);
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            Assert.Equal(new[] { secondActor.Id }, await db.Users.Where(u => u.Role == UserRole.Admin).Select(u => u.Id).ToArrayAsync());
            Assert.Equal(UserRole.Technician, (await db.Users.AsNoTracking().SingleAsync(u => u.Id == firstActor.Id)).Role);
            AssertTicketEqual(ticket, await db.Tickets.AsNoTracking().SingleAsync(t => t.Id == ticket.Id));
            using var directory = await secondClient.GetAsync("/api/users");
            Assert.Equal(HttpStatusCode.OK, directory.StatusCode);
        }
        finally
        {
            pause.Resume.TrySetResult();
            (await first.WaitAsync(TimeSpan.FromSeconds(25))).Dispose();
            if (second is not null) (await second.WaitAsync(TimeSpan.FromSeconds(25))).Dispose();
        }
    }

    private async Task AssertSerializedUserEmailUpdatesAsync()
    {
        var firstUser = await SeedHttpUserAsync();
        var secondUser = await SeedHttpUserAsync();
        var target = NewUser();
        var pause = new PauseRoleOrAssignmentSave(firstUser.Id, -1, false);
        var probe = new UserTableLockProbe();
        using var firstFactory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<HelpDeskDbContext>(options => options.AddInterceptors(pause))));
        using var secondFactory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<HelpDeskDbContext>(options => options.AddInterceptors(probe))));
        using var firstClient = await CreateAdminClientAsync(firstFactory);
        using var secondClient = await CreateAdminClientAsync(secondFactory);
        var body = new { target.Name, target.Email, role = "User" };
        var first = firstClient.PutAsJsonAsync($"/api/users/{firstUser.Id}", body);
        Task<HttpResponseMessage>? second = null;
        try
        {
            var holder = await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            second = secondClient.PutAsJsonAsync($"/api/users/{secondUser.Id}", body);
            var waiter = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await AssertBlockedByAsync(waiter, holder);
            Assert.False(second.IsCompleted);
            pause.Resume.TrySetResult();
            using var firstResponse = await first.WaitAsync(TimeSpan.FromSeconds(15));
            using var secondResponse = await second.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
            await AssertUserStoredAsync(new(secondUser.Id, secondUser.Name, secondUser.Email, secondUser.Role, secondUser.CreatedAt));
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            Assert.Equal(1, await db.Users.CountAsync(u => u.Email == target.Email));
        }
        finally
        {
            pause.Resume.TrySetResult();
            (await first.WaitAsync(TimeSpan.FromSeconds(25))).Dispose();
            if (second is not null) (await second.WaitAsync(TimeSpan.FromSeconds(25))).Dispose();
        }
    }

    private static async Task AssertLastAdminConflictAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "code", "error" }, json.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
        Assert.Equal("last_admin", json.RootElement.GetProperty("code").GetString());
        Assert.Equal("No se puede cambiar el rol del último administrador.", json.RootElement.GetProperty("error").GetString());
    }

    private sealed class UserTableLockProbe : DbCommandInterceptor
    {
        public TaskCompletionSource<int> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("LOCK TABLE public.\"Users\" IN SHARE ROW EXCLUSIVE MODE", StringComparison.Ordinal))
                Reached.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
            return ValueTask.FromResult(result);
        }
    }
}
