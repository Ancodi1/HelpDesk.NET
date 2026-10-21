using HelpDesk.Api.Data;
using HelpDesk.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace HelpDesk.Tests;

public class UserModelTests
{
    [Fact]
    public void RolesAndDefaults_AreExplicit()
    {
        Assert.Equal(new[] { "User", "Technician", "Admin" }, Enum.GetNames<UserRole>());
        var before = DateTime.UtcNow;
        var user = new User();
        Assert.Equal(UserRole.User, user.Role);
        Assert.Equal(0, user.Id);
        Assert.Equal(DateTimeKind.Utc, user.CreatedAt.Kind);
        Assert.InRange(user.CreatedAt, before, DateTime.UtcNow);
    }

    [Theory]
    [InlineData("  Person@Example.COM  ", "person@example.com")]
    [InlineData("person@example.com", "person@example.com")]
    [InlineData("\tPerson@Example.COM\n", "person@example.com")]
    public void Email_NormalizesEveryAssignment(string input, string expected)
    {
        var user = new User { Email = "previous@example.com" };
        user.Email = input;
        Assert.Equal(expected, user.Email);
        user.Email = user.Email;
        Assert.Equal(expected, user.Email);
    }

    [Fact]
    public void Model_MapsRequiredPropertiesUniqueEmailAndUtcTimestamp()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(User))!;
        Assert.Equal("Users", entity.GetTableName());
        Assert.Equal(nameof(User.Id), Assert.Single(entity.FindPrimaryKey()!.Properties).Name);
        Assert.Equal(ValueGenerated.OnAdd, entity.FindProperty(nameof(User.Id))!.ValueGenerated);
        var name = entity.FindProperty(nameof(User.Name))!;
        Assert.False(name.IsNullable);
        Assert.Equal(100, name.GetMaxLength());
        var email = entity.FindProperty(nameof(User.Email))!;
        Assert.False(email.IsNullable);
        Assert.Equal(254, email.GetMaxLength());
        var index = Assert.Single(entity.GetIndexes());
        Assert.True(index.IsUnique);
        Assert.Equal(nameof(User.Email), Assert.Single(index.Properties).Name);
        Assert.Equal("timestamp with time zone", entity.FindProperty(nameof(User.CreatedAt))!.GetColumnType());
        Assert.Empty(entity.GetForeignKeys());
        Assert.Empty(db.Model.FindEntityType(typeof(Ticket))!.GetForeignKeys());
        // Los CHECK se conservan en el modelo de diseño, no en el modelo optimizado de ejecución.
        var design = db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;
        Assert.Equal("\"Email\" = lower(btrim(\"Email\"))",
            Assert.Single(design.FindEntityType(typeof(User))!.GetCheckConstraints()).Sql);
    }

    [Theory]
    [InlineData(UserRole.User, "User")]
    [InlineData(UserRole.Technician, "Technician")]
    [InlineData(UserRole.Admin, "Admin")]
    public void Model_ConvertsRolesToText(UserRole role, string text)
    {
        using var db = CreateContext();
        var property = db.Model.FindEntityType(typeof(User))!.FindProperty(nameof(User.Role))!;
        Assert.Equal(UserRole.User, property.GetDefaultValue());
        Assert.Equal(UserRole.User, property.Sentinel);
        var converter = property.GetTypeMapping().Converter!;
        Assert.Equal(text, converter.ConvertToProvider(role));
        Assert.Equal(role, converter.ConvertFromProvider(text));
    }

    private static HelpDeskDbContext CreateContext() => new(new DbContextOptionsBuilder<HelpDeskDbContext>()
        .UseNpgsql("Host=localhost;Database=helpdesk_test_db").Options);
}
