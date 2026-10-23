using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HelpDesk.Api.Data;
using HelpDesk.Api.DTOs;
using HelpDesk.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.Tests.Integration;

public partial class TicketEndpointsTests
{
    private async Task<User> SeedHttpUserAsync(UserRole role = UserRole.User)
    {
        var user = NewUser(role);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await db.Entry(user).ReloadAsync();
        return user;
    }

    private static async Task<UserResponseDto> ReadUserResponseAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "createdAt", "email", "id", "name", "role" },
            json.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
        Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("role").ValueKind);
        var user = await response.Content.ReadFromJsonAsync<UserResponseDto>(JsonOptions);
        Assert.NotNull(user);
        return user;
    }

    private async Task AssertUserStoredAsync(UserResponseDto expected)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var stored = await db.Users.AsNoTracking().SingleAsync(u => u.Id == expected.Id);
        Assert.Equal(expected.Name, stored.Name);
        Assert.Equal(expected.Email, stored.Email);
        Assert.Equal(expected.Role, stored.Role);
        Assert.Equal(expected.CreatedAt.Ticks / 10, stored.CreatedAt.Ticks / 10);
        Assert.Equal(DateTimeKind.Utc, stored.CreatedAt.Kind);
    }

    [Fact]
    public async Task GetUsers_ReturnsOrderedDtosIncludingPersistedUsersAndPreservesTicket()
    {
        var ticket = await CreateTicketAsync();
        var first = await SeedHttpUserAsync(UserRole.Admin);
        var second = await SeedHttpUserAsync(UserRole.Technician);
        using var client = await CreateAdminClientAsync();
        using var response = await client.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var users = await response.Content.ReadFromJsonAsync<List<UserResponseDto>>(JsonOptions);
        Assert.NotNull(users);
        Assert.Equal(users.OrderBy(u => u.Id).Select(u => u.Id), users.Select(u => u.Id));
        Assert.Contains(users, u => u.Id == first.Id && u.Role == UserRole.Admin);
        Assert.Contains(users, u => u.Id == second.Id && u.Role == UserRole.Technician);
        Assert.Equal(users.Count, users.Select(u => u.Id).Distinct().Count());
        await AssertStoredAndReturnedAsync(ticket);
    }

    [Fact]
    public async Task GetUser_UnknownId_ReturnsNotFound()
    {
        await AssertAbsentUserAsync(-1);
        using var client = await CreateAdminClientAsync();
        using var response = await client.GetAsync("/api/users/-1");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    // Fuerza a ambas peticiones a superar AnyAsync antes de intentar guardar.
    private sealed class UserSaveBarrier(string email) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<User>().Any(e => e.Entity.Email == email))
            {
                if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }
}
