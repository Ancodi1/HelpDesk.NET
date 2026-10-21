using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HelpDesk.Api.Data;
using HelpDesk.Api.DTOs;
using HelpDesk.Api.Models;
using Microsoft.AspNetCore.TestHost;
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

    [Theory]
    [InlineData(null, UserRole.User)]
    [InlineData("User", UserRole.User)]
    [InlineData("Technician", UserRole.Technician)]
    [InlineData("Admin", UserRole.Admin)]
    public async Task PostUser_ReturnsCreatedLocationAndPersistsDto(string? role, UserRole expected)
    {
        var user = NewUser();
        var body = new Dictionary<string, object?>
        {
            ["name"] = user.Name, ["email"] = $"  {user.Email.ToUpperInvariant()}  ",
            ["id"] = -1, ["createdAt"] = DateTime.UnixEpoch
        };
        if (role is not null) body["role"] = role;
        using var client = fixture.Factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/users", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var returned = await ReadUserResponseAsync(response);
        Assert.True(returned.Id > 0);
        Assert.Equal(user.Name, returned.Name);
        Assert.Equal(user.Email, returned.Email);
        Assert.Equal(expected, returned.Role);
        Assert.Equal(DateTimeKind.Utc, returned.CreatedAt.Kind);
        Assert.NotEqual(DateTime.UnixEpoch, returned.CreatedAt);
        Assert.Equal($"/api/users/{returned.Id}", response.Headers.Location?.OriginalString);
        await AssertUserStoredAsync(returned);
        using var get = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var fetched = await ReadUserResponseAsync(get);
        Assert.Equal(returned with { CreatedAt = fetched.CreatedAt }, fetched);
        Assert.Equal(returned.CreatedAt.Ticks / 10, fetched.CreatedAt.Ticks / 10);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task User_ExactLengthLimits_AreAcceptedAndPersisted(string method)
    {
        var user = method == "PUT" ? await SeedHttpUserAsync() : NewUser();
        var email = Guid.NewGuid().ToString("N") + new string('a', 32) + "@" +
                    new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 61);
        ownedUserEmails.Add(email);
        using var client = fixture.Factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), method == "PUT" ? $"/api/users/{user.Id}" : "/api/users");
        request.Content = JsonContent.Create(new { name = new string('n', 100), email, role = "User" });
        using var response = await client.SendAsync(request);
        Assert.Equal(method == "PUT" ? HttpStatusCode.OK : HttpStatusCode.Created, response.StatusCode);
        var returned = await ReadUserResponseAsync(response);
        Assert.Equal(100, returned.Name.Length);
        Assert.Equal(254, returned.Email.Length);
        await AssertUserStoredAsync(returned);
    }

    [Fact]
    public async Task GetUsers_ReturnsOrderedDtosIncludingPersistedUsersAndPreservesTicket()
    {
        var ticket = await CreateTicketAsync();
        var first = await SeedHttpUserAsync(UserRole.Admin);
        var second = await SeedHttpUserAsync(UserRole.Technician);
        using var client = fixture.Factory.CreateClient();
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

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task User_UnknownId_ReturnsNotFound(string method)
    {
        var control = await SeedHttpUserAsync();
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            Assert.False(await db.Users.AnyAsync(u => u.Id == -1));
        }
        using var client = fixture.Factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/users/-1");
        if (method == "PUT") request.Content = JsonContent.Create(new { name = "Nombre", email = control.Email, role = "User" });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
        await AssertUserStoredAsync(new(control.Id, control.Name, control.Email, control.Role, control.CreatedAt));
    }

    [Theory]
    [InlineData("User", UserRole.User)]
    [InlineData("Technician", UserRole.Technician)]
    [InlineData("Admin", UserRole.Admin)]
    public async Task PutUser_UpdatesOnlyEditableFields(string role, UserRole expected)
    {
        var user = await SeedHttpUserAsync(UserRole.Technician);
        var replacement = NewUser();
        using var client = fixture.Factory.CreateClient();
        using var response = await client.PutAsJsonAsync($"/api/users/{user.Id}", new
        {
            name = "Nombre actualizado", email = $" {replacement.Email.ToUpperInvariant()} ", role,
            id = -99, createdAt = DateTime.UnixEpoch
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var returned = await ReadUserResponseAsync(response);
        Assert.Equal(user.Id, returned.Id);
        Assert.Equal(user.CreatedAt, returned.CreatedAt);
        Assert.Equal("Nombre actualizado", returned.Name);
        Assert.Equal(replacement.Email, returned.Email);
        Assert.Equal(expected, returned.Role);
        await AssertUserStoredAsync(returned);
        using var get = await client.GetAsync($"/api/users/{user.Id}");
        Assert.Equal(returned, await ReadUserResponseAsync(get));
    }

    [Fact]
    public async Task PutUser_SameEmailWithDifferentCase_IsAllowed()
    {
        var user = await SeedHttpUserAsync();
        using var client = fixture.Factory.CreateClient();
        for (var i = 0; i < 2; i++)
        {
            using var response = await client.PutAsJsonAsync($"/api/users/{user.Id}",
                new { user.Name, email = $" {user.Email.ToUpperInvariant()} ", role = "User" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var returned = await ReadUserResponseAsync(response);
            Assert.Equal(user.Email, returned.Email);
            Assert.Equal(user.CreatedAt, returned.CreatedAt);
            await AssertUserStoredAsync(returned);
        }
    }

    public static IEnumerable<object[]> InvalidUserBodies()
    {
        string[] bodies =
        [
            "{}", "{\"name\":null,\"email\":\"__EMAIL__\"}",
            "{\"name\":\"\",\"email\":\"__EMAIL__\"}", "{\"name\":\"   \",\"email\":\"__EMAIL__\"}",
            JsonSerializer.Serialize(new { name = new string('n', 101), email = "__EMAIL__", role = "User" }),
            "{\"name\":\"Nombre\",\"email\":null,\"role\":\"User\"}",
            "{\"name\":\"Nombre\",\"email\":\"\",\"role\":\"User\"}",
            "{\"name\":\"Nombre\",\"email\":\"   \",\"role\":\"User\"}",
            "{\"name\":\"Nombre\",\"email\":\"invalid\",\"role\":\"User\"}",
            "{\"name\":\"Nombre\",\"email\":\"person @example.com\",\"role\":\"User\"}",
            JsonSerializer.Serialize(new { name = "Nombre", email = new string('a', 255), role = "User" }),
            "{\"name\":\"Nombre\",\"email\":\"__EMAIL__\",\"role\":null}",
            "{\"name\":\"Nombre\",\"email\":\"__EMAIL__\",\"role\":\"\"}",
            "{\"name\":\"Nombre\",\"email\":\"__EMAIL__\",\"role\":\"user\"}",
            "{\"name\":\"Nombre\",\"email\":\"__EMAIL__\",\"role\":\"Inventado\"}",
            "{\"name\":\"Nombre\",\"email\":\"__EMAIL__\",\"role\":\"0\"}",
            "{\"name\":\"Nombre\",\"email\":\"__EMAIL__\",\"role\":1}",
            "{\"name\":1,\"email\":\"__EMAIL__\"}",
            "{\"name\":\"Nombre\",\"email\":123}", "{\"name\":", "[]", "null", ""
        ];
        foreach (var method in new[] { "POST", "PUT" })
            foreach (var body in bodies) yield return [method, body];
    }

    [Theory]
    [MemberData(nameof(InvalidUserBodies))]
    public async Task User_InvalidBody_ReturnsJsonBadRequestWithoutWrites(string method, string payload)
    {
        var user = method == "PUT" ? await SeedHttpUserAsync(UserRole.Admin) : NewUser();
        using var client = fixture.Factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), method == "PUT" ? $"/api/users/{user.Id}" : "/api/users");
        request.Content = new StringContent(payload.Replace("__EMAIL__", user.Email), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);
        // Si una regresión acepta una entrada inválida, registrar su ID antes de fallar.
        if (method == "POST" && response.StatusCode == HttpStatusCode.Created)
        {
            var unexpected = await response.Content.ReadFromJsonAsync<UserResponseDto>(JsonOptions);
            if (unexpected is not null) user.Id = unexpected.Id;
        }
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        await AssertHasErrorAsync(response);
        if (method == "PUT") await AssertUserStoredAsync(new(user.Id, user.Name, user.Email, user.Role, user.CreatedAt));
        else
        {
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            Assert.False(await db.Users.AnyAsync(u => u.Email == user.Email));
        }
    }

    [Fact]
    public async Task PutUser_OmittedRole_ReturnsBadRequest()
    {
        var user = await SeedHttpUserAsync(UserRole.Admin);
        using var client = fixture.Factory.CreateClient();
        using var response = await client.PutAsJsonAsync($"/api/users/{user.Id}", new { user.Name, user.Email });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertHasErrorAsync(response);
        await AssertUserStoredAsync(new(user.Id, user.Name, user.Email, user.Role, user.CreatedAt));
    }

    [Theory]
    [InlineData("POST", false)]
    [InlineData("POST", true)]
    [InlineData("PUT", false)]
    [InlineData("PUT", true)]
    public async Task User_DuplicateEmail_ReturnsConflictWithoutChanges(string method, bool changeCase)
    {
        var existing = await SeedHttpUserAsync(UserRole.Admin);
        var control = await SeedHttpUserAsync(UserRole.Technician);
        var email = changeCase ? $" {existing.Email.ToUpperInvariant()} " : existing.Email;
        using var client = fixture.Factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), method == "POST" ? "/api/users" : $"/api/users/{control.Id}");
        request.Content = JsonContent.Create(new { name = "No se guarda", email, role = "User" });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertHasErrorAsync(response);
        await AssertUserStoredAsync(new(existing.Id, existing.Name, existing.Email, existing.Role, existing.CreatedAt));
        await AssertUserStoredAsync(new(control.Id, control.Name, control.Email, control.Role, control.CreatedAt));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task User_ConcurrentDuplicateAfterPrecheck_ReturnsOneSuccessAndOneConflict(string method)
    {
        var target = NewUser();
        var first = method == "PUT" ? await SeedHttpUserAsync() : null;
        var second = method == "PUT" ? await SeedHttpUserAsync() : null;
        var barrier = new UserSaveBarrier(target.Email);
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<HelpDeskDbContext>(options => options.AddInterceptors(barrier))));
        using var client = factory.CreateClient();
        async Task<HttpResponseMessage> SendAsync(User? user) => method == "PUT"
            ? await client.PutAsJsonAsync($"/api/users/{user!.Id}", new { name = "Concurrente", email = target.Email, role = "User" })
            : await client.PostAsJsonAsync("/api/users", new { name = "Concurrente", email = target.Email, role = "User" });
        var responses = await Task.WhenAll(SendAsync(first), SendAsync(second));
        try
        {
            var success = method == "PUT" ? HttpStatusCode.OK : HttpStatusCode.Created;
            Assert.Single(responses, r => r.StatusCode == success);
            var conflict = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
            await AssertHasErrorAsync(conflict);
            await AssertUserStoredAsync(await ReadUserResponseAsync(responses.Single(r => r.StatusCode == success)));
            if (method == "PUT")
            {
                var unchanged = responses[0].StatusCode == HttpStatusCode.Conflict ? first! : second!;
                await AssertUserStoredAsync(new(unchanged.Id, unchanged.Name, unchanged.Email, unchanged.Role, unchanged.CreatedAt));
            }
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            Assert.Equal(1, await db.Users.CountAsync(u => u.Email == target.Email));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
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
