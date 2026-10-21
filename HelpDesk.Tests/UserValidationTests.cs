using HelpDesk.Api.DTOs;
using HelpDesk.Api.Models;
using HelpDesk.Api.Validation;

namespace HelpDesk.Tests;

public class UserValidationTests
{
    [Theory]
    [InlineData("User", UserRole.User)]
    [InlineData("Technician", UserRole.Technician)]
    [InlineData("Admin", UserRole.Admin)]
    public void ValidInput_NormalizesEmailAndParsesRole(string text, UserRole expected)
    {
        Assert.Null(UserValidation.Validate(" Nombre ", "  Person@Example.COM  ", text, out var email, out var role));
        Assert.Equal("person@example.com", email);
        Assert.Equal(expected, role);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("user")]
    [InlineData("0")]
    [InlineData("3")]
    [InlineData("Inventado")]
    [InlineData("User, Admin")]
    public void InvalidRole_ReturnsError(string? role)
    {
        Assert.NotNull(UserValidation.Validate("Nombre", "person@example.com", role, out _, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\n")]
    public void MissingName_ReturnsError(string? name)
    {
        Assert.NotNull(UserValidation.Validate(name, "person@example.com", "User", out _, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("person")]
    [InlineData("person@")]
    [InlineData("@example.com")]
    [InlineData("person@example")]
    [InlineData("person @example.com")]
    [InlineData("person@exam ple.com")]
    [InlineData("Nombre <person@example.com>")]
    [InlineData("person@example.com,other@example.com")]
    [InlineData("person@example..com")]
    [InlineData("person@-example.com")]
    [InlineData("person@example-.com")]
    [InlineData("person@exam_ple.com")]
    [InlineData("person@example.com\r\nBcc:other@example.com")]
    public void InvalidEmail_ReturnsError(string? email)
    {
        Assert.NotNull(UserValidation.Validate("Nombre", email, "User", out _, out _));
    }

    [Fact]
    public void LengthLimits_AcceptBoundaryAndRejectOverflow()
    {
        var email = new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 61);
        Assert.Equal(254, email.Length);
        Assert.Null(UserValidation.Validate(new string('n', 100), email, "User", out _, out _));
        Assert.NotNull(UserValidation.Validate(new string('n', 101), email, "User", out _, out _));
        Assert.NotNull(UserValidation.Validate("Nombre", "x" + email, "User", out _, out _));
    }

    [Fact]
    public void OnlyCreateDto_DefaultsRoleToUser()
    {
        Assert.Equal("User", new CreateUserDto().Role);
        Assert.Null(new UpdateUserDto().Role);
    }
}
