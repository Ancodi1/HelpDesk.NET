using HelpDesk.Api.Data;
using HelpDesk.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HelpDesk.Tests.Integration;

// Comparte fixture, serialización de ejecución y limpieza de la clase existente.
public partial class TicketEndpointsTests
{
    private readonly HashSet<string> ownedUserEmails = [];
    private readonly List<User> ownedUsers = [];

    private User NewUser(UserRole role = UserRole.User)
    {
        var email = $"integration-{Guid.NewGuid():N}@example.invalid";
        ownedUserEmails.Add(email);
        var user = new User { Name = "Usuario de integración", Email = email, Role = role };
        ownedUsers.Add(user);
        return user;
    }

    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Admin)]
    public async Task User_PersistsRoleAndUtcTimestamp_WithoutChangingTicket(UserRole role)
    {
        var control = await CreateTicketAsync(TicketStatus.Resolved, TicketPriority.High, TicketCategory.Network);
        var user = NewUser(role);
        user.Name = new string('n', 100);
        user.Email = new string('a', 254 - user.Email.Length) + user.Email;
        ownedUserEmails.Add(user.Email);
        user.CreatedAt = new DateTime(2026, 10, 21, 10, 20, 30, DateTimeKind.Utc).AddTicks(1234560);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync();
            Assert.True(user.Id > 0);
            // Comprueba la representación almacenada, sin el conversor enum de EF.
            var storedRole = await db.Database.SqlQuery<string>($"SELECT \"Role\" AS \"Value\" FROM \"Users\" WHERE \"Id\" = {user.Id}").SingleAsync();
            Assert.Equal(role.ToString(), storedRole);
        }
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            var stored = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
            Assert.Equal(user.Name, stored.Name);
            Assert.Equal(user.Email, stored.Email);
            Assert.Equal(role, stored.Role);
            Assert.Equal(user.CreatedAt, stored.CreatedAt);
            Assert.Equal(DateTimeKind.Utc, stored.CreatedAt.Kind);
        }
        await AssertStoredAndReturnedAsync(control);
    }

    [Fact]
    public async Task User_DatabaseRoleDefault_IsUser()
    {
        var user = NewUser();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Users\" (\"Name\", \"Email\", \"CreatedAt\") VALUES ({user.Name}, {user.Email}, {user.CreatedAt})");
        var stored = await db.Users.AsNoTracking().SingleAsync(u => u.Email == user.Email);
        Assert.Equal(UserRole.User, stored.Role);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task User_DuplicateEmail_IsRejectedIncludingDifferentCase(bool differentCase)
    {
        var first = NewUser();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        db.Users.Add(first);
        await db.SaveChangesAsync();
        var duplicate = NewUser();
        duplicate.Email = differentCase ? $"  {first.Email.ToUpperInvariant()}  " : first.Email;
        db.Users.Add(duplicate);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Users.CountAsync(u => u.Email == first.Email));
    }

    [Fact]
    public async Task User_EmailUpdate_IsNormalizedAndDuplicateIsRejected()
    {
        var first = NewUser();
        var second = NewUser();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        db.Users.AddRange(first, second);
        await db.SaveChangesAsync();
        second.Email = first.Email.ToUpperInvariant();
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Theory]
    [InlineData("Name", "null", PostgresErrorCodes.NotNullViolation)]
    [InlineData("Email", "null", PostgresErrorCodes.NotNullViolation)]
    [InlineData("Name", "long", PostgresErrorCodes.StringDataRightTruncation)]
    [InlineData("Email", "long", PostgresErrorCodes.StringDataRightTruncation)]
    public async Task User_InvalidRequiredOrLength_IsRejected(string property, string invalid, string sqlState)
    {
        var user = NewUser();
        if (property == "Name") user.Name = invalid == "null" ? null! : new string('n', 101);
        else user.Email = invalid == "null" ? null! : user.Email + new string('e', 255 - user.Email.Length);
        if (user.Email is not null) ownedUserEmails.Add(user.Email);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        db.Users.Add(user);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(sqlState, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task User_DirectSqlNonCanonicalEmail_IsRejected(bool upperCase)
    {
        var user = NewUser();
        var email = upperCase ? user.Email.ToUpperInvariant() : $" {user.Email} ";
        ownedUserEmails.Add(email);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"Users\" (\"Name\", \"Email\", \"CreatedAt\") VALUES ({user.Name}, {email}, {user.CreatedAt})"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task User_NonUtcTimestamp_IsRejected()
    {
        var user = NewUser();
        user.CreatedAt = DateTime.SpecifyKind(DateTime.UnixEpoch, DateTimeKind.Unspecified);
        if (user.Email is not null) ownedUserEmails.Add(user.Email);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        db.Users.Add(user);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.IsType<ArgumentException>(error.InnerException);
    }

    private async Task CleanupUsersAsync()
    {
        if (ownedUserEmails.Count == 0) return;
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var emails = ownedUserEmails.ToArray();
        var ids = ownedUsers.Where(u => u.Id > 0).Select(u => u.Id).ToArray();
        await db.Users.Where(u => ids.Contains(u.Id) || emails.Contains(u.Email)).ExecuteDeleteAsync();
        Assert.False(await db.Users.AnyAsync(u => ids.Contains(u.Id) || emails.Contains(u.Email)));
    }
}
