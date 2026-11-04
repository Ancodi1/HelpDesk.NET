using HelpDesk.Api.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace HelpDesk.Tests;

public class TicketActivityTests
{
    [Fact]
    public void MigrationAddsOnlyActivitiesWithRestrictAndCursorIndex()
    {
        var migration = new AddTicketActivities();
        Assert.All(migration.UpOperations, op => Assert.True(op is CreateTableOperation or CreateIndexOperation));
        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("TicketActivities", table.Name);
        Assert.Equal(2, table.ForeignKeys.Count);
        Assert.All(table.ForeignKeys, fk => Assert.Equal(ReferentialAction.Restrict, fk.OnDelete));
        Assert.Equal(4, table.CheckConstraints.Count);
        Assert.Contains(table.Columns, c => c.Name == "CreatedAt" && c.ColumnType == "timestamp with time zone");
        Assert.Contains(migration.UpOperations.OfType<CreateIndexOperation>(), i => i.Columns.SequenceEqual(new[] { "TicketId", "Id" }));
    }
}
