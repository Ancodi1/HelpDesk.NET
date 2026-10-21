using HelpDesk.Api.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace HelpDesk.Tests;

public class TicketUserRelationMigrationTests
{
    [Fact]
    public void Migration_OnlyAddsNullableColumnsIndexesAndRestrictiveForeignKeys()
    {
        var migration = new AddTicketUserRelations();
        var operations = migration.UpOperations;
        Assert.Equal(6, operations.Count);
        Assert.All(operations, op => Assert.True(op is AddColumnOperation or CreateIndexOperation or AddForeignKeyOperation));
        var columns = operations.OfType<AddColumnOperation>().ToList();
        Assert.Equal(new[] { "AssignedToUserId", "CreatedByUserId" }, columns.Select(c => c.Name).OrderBy(n => n));
        foreach (var column in columns)
        {
            Assert.Equal("Tickets", column.Table);
            Assert.Equal(typeof(int), column.ClrType);
            Assert.True(column.IsNullable);
            Assert.Null(column.DefaultValue);
            Assert.Null(column.DefaultValueSql);
            var index = Assert.Single(operations.OfType<CreateIndexOperation>(), i => i.Columns.SequenceEqual(new[] { column.Name }));
            Assert.Equal("Tickets", index.Table);
            Assert.False(index.IsUnique);
            var fk = Assert.Single(operations.OfType<AddForeignKeyOperation>(), f => f.Columns.SequenceEqual(new[] { column.Name }));
            Assert.Equal("Tickets", fk.Table);
            Assert.Equal("Users", fk.PrincipalTable);
            Assert.Equal(new[] { "Id" }, fk.PrincipalColumns);
            Assert.Equal(ReferentialAction.Restrict, fk.OnDelete);
        }
        Assert.Equal(6, migration.DownOperations.Count);
        Assert.All(migration.DownOperations, op => Assert.True(op is DropForeignKeyOperation or DropIndexOperation or DropColumnOperation));
        Assert.Equal(2, migration.DownOperations.OfType<DropColumnOperation>().Count());
    }
}
