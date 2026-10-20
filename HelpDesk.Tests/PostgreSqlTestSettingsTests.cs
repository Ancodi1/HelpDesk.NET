using HelpDesk.Tests.Integration;

namespace HelpDesk.Tests;

public class PostgreSqlTestSettingsTests
{
    [Theory]
    [InlineData("Include Error Detail")]
    [InlineData("Persist Security Info")]
    [InlineData("Enlist")]
    public void Validate_InternalSafetyOptionTrue_IsRejected(string option)
    {
        var error = Assert.Throws<InvalidOperationException>(() => PostgreSqlTestSettings.Validate(
            $"Host=localhost;Database=helpdesk_test_db;Username=helpdesk_test_user;Password=placeholder;{option}=true"));

        Assert.Contains("seguridad incorrecta", error.Message);
        Assert.DoesNotContain("placeholder", error.Message);
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData("Password=\"fake;unterminated", "Sintaxis no válida")]
    [InlineData("Password=fake;unexpected", "Sintaxis no válida")]
    [InlineData("Password=fake;Options=anything", "opción no permitida")]
    [InlineData("Password=fake;Port=invalid", "valor de opción no válido")]
    [InlineData("Password=fake;Host=remote.example", "seguridad incorrecta")]
    public void Validate_InvalidInput_DistinguishesReasonWithoutExposingValues(string suffix, string expected)
    {
        var error = Assert.Throws<InvalidOperationException>(() => PostgreSqlTestSettings.Validate(
            $"Host=localhost;Database=helpdesk_test_db;Username=helpdesk_test_user;{suffix}"));

        Assert.Contains(expected, error.Message);
        Assert.DoesNotContain("fake", error.Message);
        Assert.DoesNotContain("remote.example", error.Message);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void Validate_NormalizedOutput_CanBeValidatedAgain()
    {
        var first = PostgreSqlTestSettings.Validate("Host=localhost;Database=helpdesk_test_db;Username=helpdesk_test_user;Password=placeholder");

        var second = PostgreSqlTestSettings.Validate(first);

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("fake;password")]
    [InlineData("fake\"password")]
    [InlineData("fake'password")]
    [InlineData("fake=password")]
    [InlineData("fake;\"quoted\"='value'")]
    public void Validate_QuotedSpecialPassword_PreservesValue(string password)
    {
        // El builder cita el valor según la sintaxis ADO.NET; no concatenar contraseñas.
        var builder = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = "localhost", Database = "helpdesk_test_db",
            Username = "helpdesk_test_user", Password = password
        };

        var result = PostgreSqlTestSettings.Validate(builder.ConnectionString);
        var parsed = new Npgsql.NpgsqlConnectionStringBuilder(result);

        Assert.Equal(password, parsed.Password);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Host=localhost;Database=helpdesk_db;Username=helpdesk_test_user;Password=placeholder")]
    [InlineData("Host=localhost;Database=helpdesk_test_db;Username=postgres;Password=placeholder")]
    [InlineData("Host=remote.example;Database=helpdesk_test_db;Username=helpdesk_test_user;Password=placeholder")]
    [InlineData("Host=localhost;Database=helpdesk_test_db;Username=helpdesk_test_user")]
    [InlineData("Host=localhost;Database=helpdesk_test_db;Username=helpdesk_test_user;Password=placeholder;Options=-c search_path=other")]
    public void Validate_UnsafeConfiguration_ThrowsWithoutExposingConfiguration(string? value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => PostgreSqlTestSettings.Validate(value));

        Assert.DoesNotContain("placeholder", error.Message);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void Validate_ExplicitLocalTestDestination_AcceptsWithoutConnecting()
    {
        var value = PostgreSqlTestSettings.Validate("Host=localhost;Database=helpdesk_test_db;Username=helpdesk_test_user;Password=placeholder");

        var parsed = new Npgsql.NpgsqlConnectionStringBuilder(value);
        Assert.Equal("helpdesk_test_db", parsed.Database);
        Assert.Equal("helpdesk_test_user", parsed.Username);
        Assert.False(parsed.IncludeErrorDetail);
    }
}
