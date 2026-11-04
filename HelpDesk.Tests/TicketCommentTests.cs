using HelpDesk.Api.Migrations;
using HelpDesk.Api.Validation;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace HelpDesk.Tests;

public class TicketCommentTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\n\r")]
    [InlineData("\u0085\u2003\u3000")]
    [InlineData("text\0text")]
    public void InvalidCommentIsRejected(string? content) => Assert.NotNull(TicketCommentValidation.Validate(content));

    [Fact]
    public void LimitCountsUtf16AndPreservesPlainText()
    {
        Assert.Null(TicketCommentValidation.Validate(new string('x', 4000)));
        Assert.NotNull(TicketCommentValidation.Validate(new string('x', 4001)));
        Assert.Null(TicketCommentValidation.Validate(string.Concat(Enumerable.Repeat("😀", 2000))));
        Assert.NotNull(TicketCommentValidation.Validate(string.Concat(Enumerable.Repeat("😀", 2001))));
        Assert.Null(TicketCommentValidation.Validate("  <script>text</script>\nline  "));
    }

    [Fact]
    public void MigrationIsAdditiveAndNeverCascades()
    {
        var migration = new AddTicketComments();
        Assert.All(migration.UpOperations, operation => Assert.True(operation is CreateTableOperation or CreateIndexOperation));
        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("TicketComments", table.Name);
        Assert.Equal(2, table.ForeignKeys.Count);
        Assert.All(table.ForeignKeys, fk => Assert.Equal(ReferentialAction.Restrict, fk.OnDelete));
        Assert.Contains(table.Columns, c => c.Name == "Content" && c.MaxLength == 4000 && !c.IsNullable);
        Assert.Contains(table.Columns, c => c.Name == "CreatedAt" && c.ColumnType == "timestamp with time zone");
        Assert.Equal(2, table.CheckConstraints.Count);
        Assert.Contains(migration.UpOperations.OfType<CreateIndexOperation>(), index => index.Columns.SequenceEqual(new[] { "TicketId", "Id" }));
        Assert.Equal("TicketComments", Assert.IsType<DropTableOperation>(Assert.Single(migration.DownOperations)).Name);
    }
}
