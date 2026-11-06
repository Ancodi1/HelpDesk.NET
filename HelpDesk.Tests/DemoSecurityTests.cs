using System.Security.Cryptography;
using HelpDesk.Api.Demo;
using HelpDesk.Demo;
using HelpDesk.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HelpDesk.Tests;

public class DemoSecurityTests
{
    private static string Password() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
    internal static string Connection(string database = DemoTarget.Database, string username = DemoTarget.Username) =>
        new NpgsqlConnectionStringBuilder { Host = "127.0.0.1", Port = 1, Database = database, Username = username, Password = Password() }.ConnectionString;

    [Theory]
    [InlineData("helpdesk_db", "helpdesk_demo_user")]
    [InlineData("helpdesk_test_db", "helpdesk_test_user")]
    [InlineData("helpdesk_demo_db", "postgres")]
    [InlineData("helpdesk_demo_db", "helpdesk_user")]
    public void Target_RejectsNormalDatabaseAndWrongRolesBeforeConnecting(string database, string username) =>
        Assert.Throws<InvalidOperationException>(() => new DemoTarget(Connection(database, username), Guid.NewGuid().ToString()));

    [Theory]
    [InlineData("Search Path=public")]
    [InlineData("Options=-c search_path=public")]
    [InlineData("Include Error Detail=true")]
    [InlineData("Persist Security Info=true")]
    public void Target_RejectsConnectionOptions(string extra) =>
        Assert.Throws<InvalidOperationException>(() => new DemoTarget(Connection() + ";" + extra, Guid.NewGuid().ToString()));

    [Fact]
    public async Task Restore_RejectsMissingConfirmationBeforeConnection()
    {
        var target = new DemoTarget(Connection(), Guid.NewGuid().ToString());
        using var services = BootstrapSecurityTests.Services();
        var service = new DemoRestorer(services.GetService(typeof(Microsoft.AspNetCore.Identity.IPasswordHasher<User>)) as Microsoft.AspNetCore.Identity.IPasswordHasher<User> ?? throw new InvalidOperationException());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RestoreAsync(target, null, false));
        Assert.Contains("No se ha abierto", error.Message);
    }

    [Fact]
    public void DisabledMode_NeedsNoDemoConfiguration()
    {
        new DemoSettings().Validate();
        Assert.Throws<InvalidOperationException>(() => new DemoSettings { Enabled = true }.Validate());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ProtectedAccounts_RejectEveryMutationBeforeResolvingConnection(int id)
    {
        foreach (var method in new[] { "PUT", "PATCH", "DELETE", "POST" })
        {
            var context = new DefaultHttpContext();
            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "test"));
            context.Request.Path = "/api/users/" + id;
            context.Request.Method = method;
            context.Request.RouteValues["id"] = id.ToString();
            context.Response.Body = new MemoryStream();
            var called = false;
            await new DemoMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context,
                Options.Create(new DemoSettings { Enabled = true }));
            Assert.Equal(409, context.Response.StatusCode);
            Assert.False(called);
        }
    }

    [Theory]
    [InlineData("/api/auth/register")]
    [InlineData("/API/AUTH/REGISTER/")]
    [InlineData("/api/users")]
    public async Task Demo_ClosesAccountCreationInBackend(string path)
    {
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "test"));
        context.Request.Path = path;
        context.Request.Method = "POST";
        context.Response.Body = new MemoryStream();
        await new DemoMiddleware(_ => throw new InvalidOperationException()).InvokeAsync(context,
            Options.Create(new DemoSettings { Enabled = true }));
        Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task NormalMode_DoesNotInterceptAccountChanges()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "PUT";
        context.Request.Path = "/api/users/1";
        var called = false;
        await new DemoMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context, Options.Create(new DemoSettings()));
        Assert.True(called);
    }

    [Fact]
    public void Dataset_HasDistinctFictionalEmailsAndOnlyThreeCredentialsToProvision()
    {
        var users = DemoDataset.Users();
        Assert.Equal(5, users.Length);
        Assert.Equal(5, users.Select(u => u.Email).Distinct().Count());
        Assert.All(users, u => { Assert.EndsWith("@demo.helpdesk.test", u.Email); Assert.Null(u.PasswordHash); });
        Assert.Single(users, u => u.Role == UserRole.Admin);
    }
}
