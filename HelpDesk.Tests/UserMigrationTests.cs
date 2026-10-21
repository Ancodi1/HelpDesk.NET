using HelpDesk.Api.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace HelpDesk.Tests;

public class UserMigrationTests
{
    [Fact]
    public void AddUsers_OnlyCreatesUsersAndItsUniqueIndex()
    {
        var migration = new AddUsers();
        Assert.Equal(2, migration.UpOperations.Count);
        var table = Assert.IsType<CreateTableOperation>(migration.UpOperations[0]);
        Assert.Equal("Users", table.Name);
        Assert.Equal(new[] { "Id", "Name", "Email", "Role", "CreatedAt" }, table.Columns.Select(c => c.Name));
        Assert.Empty(table.ForeignKeys);
        var index = Assert.IsType<CreateIndexOperation>(migration.UpOperations[1]);
        Assert.Equal("Users", index.Table);
        Assert.Equal(new[] { "Email" }, index.Columns);
        Assert.True(index.IsUnique);
        var drop = Assert.IsType<DropTableOperation>(Assert.Single(migration.DownOperations));
        Assert.Equal("Users", drop.Name);
    }
}
