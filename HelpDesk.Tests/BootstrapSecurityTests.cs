using System.Security.Cryptography;
using HelpDesk.AdminBootstrap;
using HelpDesk.Api.Authentication;
using HelpDesk.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HelpDesk.Tests;

public class BootstrapSecurityTests
{
    internal static string TestConnection() => new NpgsqlConnectionStringBuilder
    {
        Host = "localhost", Database = "helpdesk_test_db", Username = "helpdesk_test_user",
        Password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
    }.ConnectionString;

    internal static ServiceProvider Services() => new ServiceCollection().AddHelpDeskPasswordHashing().BuildServiceProvider();

    [Theory]
    [InlineData(null, "valid@example.invalid", "valid")]
    [InlineData("", "valid@example.invalid", "valid")]
    [InlineData(" ", "valid@example.invalid", "valid")]
    [InlineData("valid", null, "valid")]
    [InlineData("valid", "invalid", "valid")]
    [InlineData("valid", "person @example.invalid", "valid")]
    [InlineData("valid", "valid@example.invalid", null)]
    [InlineData("valid", "valid@example.invalid", "")]
    [InlineData("valid", "valid@example.invalid", "short")]
    [InlineData("valid", "valid@example.invalid", "               ")]
    public void Validation_ReusesRegistrationRules(string? name, string? email, string? password)
    {
        if (password == "valid") password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        Assert.NotNull(BootstrapService.Validate(name, email, password, password));
    }

    [Fact]
    public void Validation_EnforcesExistingLimitsAndExactPasswordConfirmation()
    {
        Assert.Null(BootstrapService.Validate("Nombre", " TEST@EXAMPLE.INVALID ", new string('á', 15), new string('á', 15)));
        Assert.Null(BootstrapService.Validate(new string('n', 100), "test@example.invalid", new string('á', 128), new string('á', 128)));
        Assert.NotNull(BootstrapService.Validate(new string('n', 101), "test@example.invalid", new string('á', 15), new string('á', 15)));
        Assert.NotNull(BootstrapService.Validate("Nombre", "test@example.invalid", new string('a', 129), new string('a', 129)));
        Assert.NotNull(BootstrapService.Validate("Nombre", "test@example.invalid", new string('a', 15) + "\t", new string('a', 15) + "\t"));
        Assert.NotNull(BootstrapService.Validate("Nombre", "test@example.invalid", new string('a', 15), new string('a', 16)));
    }

    [Fact]
    public void SharedHasher_UsesSameSettingsAndVerifiesPassword()
    {
        using var services = Services();
        using var scope = services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<PasswordHasherOptions>>().Value;
        Assert.Equal(210_000, options.IterationCount);
        Assert.Equal(PasswordHasherCompatibilityMode.IdentityV3, options.CompatibilityMode);
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var user = new User();
        var hash = hasher.HashPassword(user, password);
        Assert.True(hash != password);
        Assert.True(hasher.VerifyHashedPassword(user, hash, password) == PasswordVerificationResult.Success);
    }

    [Theory]
    [InlineData("")]
    [InlineData("confirmar primer admin")]
    [InlineData("CONFIRMAR PRIMER ADMIN ")]
    [InlineData("yes")]
    [InlineData(null)]
    public async Task IncorrectConfirmation_CancelsWithoutOpeningDatabase(string? confirmation)
    {
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var console = new FakeBootstrapConsole(["helpdesk_test_db", "new@example.invalid", "Nombre", confirmation], [password, password]);
        using var services = Services();
        using var scope = services.CreateScope();
        var command = new BootstrapCommand(console, new BootstrapService(scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>()));
        Assert.Equal(2, await command.RunAsync([], TestConnection()));
        Assert.Contains("Operación cancelada", console.Output);
        Assert.True(!console.Output.Contains(password, StringComparison.Ordinal), "No imprimir contraseñas.");
    }

    [Theory]
    [InlineData("helpdesk_db")]
    [InlineData("HELPDESK_TEST_DB")]
    [InlineData("")]
    [InlineData(null)]
    public async Task WrongDatabase_CancelsBeforeReadingCredentials(string? database)
    {
        var console = new FakeBootstrapConsole([database], []);
        using var services = Services();
        using var scope = services.CreateScope();
        var command = new BootstrapCommand(console, new BootstrapService(scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>()));
        Assert.Equal(2, await command.RunAsync([], TestConnection()));
        Assert.Equal(0, console.SecretReads);
    }

    [Theory]
    [InlineData("redirected")]
    [InlineData("argument")]
    [InlineData("missing-connection")]
    [InlineData("invalid-connection")]
    [InlineData("cancel-password")]
    [InlineData("cancel-repeat")]
    [InlineData("password-mismatch")]
    [InlineData("invalid-input")]
    public async Task InvalidInvocationOrInput_IsSafeWithoutDatabase(string scenario)
    {
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var secrets = scenario switch
        {
            "cancel-password" => new string?[] { null },
            "cancel-repeat" => [secret, null],
            "password-mismatch" => [secret, secret + "other"],
            _ => [secret, secret]
        };
        var console = new FakeBootstrapConsole(["helpdesk_test_db", scenario == "invalid-input" ? "invalid" : "new@example.invalid", "Nombre"], secrets)
        { IsInteractive = scenario != "redirected" };
        using var services = Services();
        using var scope = services.CreateScope();
        var command = new BootstrapCommand(console, new BootstrapService(scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>()));
        var connection = scenario switch { "missing-connection" => null, "invalid-connection" => secret, _ => TestConnection() };
        Assert.NotEqual(0, await command.RunAsync(scenario == "argument" ? [secret] : [], connection));
        Assert.True(!console.Output.Contains(secret, StringComparison.Ordinal), "El error no debe incluir entradas sensibles.");
    }

    [Theory]
    [InlineData("Search Path", "other")]
    [InlineData("Options", "-c search_path=other")]
    [InlineData("Include Error Detail", "true")]
    [InlineData("Persist Security Info", "true")]
    [InlineData("Enlist", "true")]
    [InlineData("Host", "localhost,other-host")]
    [InlineData("Host", "localhost\nunsafe")]
    [InlineData("Database", "")]
    [InlineData("Username", "")]
    [InlineData("Password", "")]
    public void UnsafeConnection_IsRejectedWithoutExposingItsValues(string key, string value)
    {
        var builder = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = TestConnection() };
        var password = (string)builder["Password"];
        builder[key] = value;
        var error = Assert.Throws<InvalidOperationException>(() => new BootstrapConnection(builder.ConnectionString));
        Assert.Null(error.InnerException);
        Assert.True(!error.ToString().Contains(password, StringComparison.Ordinal), "La excepción no debe incluir credenciales.");
    }

    [Fact]
    public async Task Service_AlsoRequiresConfirmationsBeforeOpeningDatabase()
    {
        using var services = Services();
        using var scope = services.CreateScope();
        var service = new BootstrapService(scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>());
        var connection = new BootstrapConnection(TestConnection());
        await using var db = connection.CreateDbContext();
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        Assert.Equal(BootstrapResult.Cancelled, await service.CreateAsync(db, connection, connection.Database, "wrong", "Nombre", "new@example.invalid", password, password));
        Assert.Equal(BootstrapResult.WrongTarget, await service.CreateAsync(db, connection, "other", BootstrapService.Confirmation, "Nombre", "new@example.invalid", password, password));
        Assert.Equal(BootstrapResult.InvalidInput, await service.CreateAsync(db, connection, connection.Database, BootstrapService.Confirmation, "", "new@example.invalid", password, password));
    }

    [Fact]
    public async Task RealLinuxTerminal_HidesPasswordAndCancelsBeforeOpeningDatabase()
    {
        var connectionString = TestConnection(); // Credencial efímera ficticia; no se abre ninguna conexión.
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        await using var cli = new Integration.BootstrapTerminalProcess(connectionString);
        await cli.EnterAsync("new@example.invalid", "Nombre", password, "incorrect");
        var result = await cli.CompleteAsync();
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Operación cancelada sin crear una cuenta.", result.Output);
        foreach (var secret in new[] { password, connectionString, new NpgsqlConnectionStringBuilder(connectionString).Password! })
            Assert.True(!result.Output.Contains(secret, StringComparison.Ordinal), "La CLI real no debe mostrar secretos.");
    }
}

internal sealed class FakeBootstrapConsole(IEnumerable<string?> lines, IEnumerable<string?> secrets) : IBootstrapConsole
{
    private readonly Queue<string?> lines = new(lines);
    private readonly Queue<string?> secrets = new(secrets);
    public bool IsInteractive { get; init; } = true;
    public int SecretReads { get; private set; }
    public string Output { get; private set; } = "";
    public void WriteLine(string message) => Output += message + "\n";
    public string? ReadLine(string prompt) { WriteLine(prompt); return lines.Dequeue(); }
    public string? ReadSecret(string prompt) { SecretReads++; WriteLine(prompt); return secrets.Dequeue(); }
}
