using HelpDesk.Api.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace HelpDesk.Tests;

public class UserPasswordMigrationTests
{
    [Fact]
    public void Migration_AddsOnlyNullableHashWithoutDefaultOrDataChanges()
    {
        var migration = new AddUserPasswordHash();
        var column = Assert.IsType<AddColumnOperation>(Assert.Single(migration.UpOperations));
        Assert.Equal("Users", column.Table);
        Assert.Equal("PasswordHash", column.Name);
        Assert.Equal(typeof(string), column.ClrType);
        Assert.Equal("text", column.ColumnType);
        Assert.True(column.IsNullable);
        Assert.Null(column.DefaultValue);
        Assert.Null(column.DefaultValueSql);
        var down = Assert.IsType<DropColumnOperation>(Assert.Single(migration.DownOperations));
        Assert.Equal("Users", down.Table);
        Assert.Equal("PasswordHash", down.Name);
    }
}
