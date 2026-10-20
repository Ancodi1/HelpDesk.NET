using HelpDesk.Api.Data;
using HelpDesk.Api.DTOs;
using HelpDesk.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Tests;

public class TicketPriorityValidationTests
{
    [Theory]
    [InlineData("Low", TicketPriority.Low)]
    [InlineData("Medium", TicketPriority.Medium)]
    [InlineData("High", TicketPriority.High)]
    [InlineData("Critical", TicketPriority.Critical)]
    public void TryParse_DefinedName_ReturnsPriority(string value, TicketPriority expected)
    {
        Assert.True(TicketPriorityValidation.TryParse(value, out var priority));
        Assert.Equal(expected, priority);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("low")]
    [InlineData("1")]
    [InlineData("-1")]
    [InlineData("4")]
    [InlineData("High, Critical")]
    [InlineData("Inventado")]
    public void TryParse_InvalidValue_ReturnsFalse(string? value)
    {
        Assert.False(TicketPriorityValidation.TryParse(value, out _));
    }

    [Fact]
    public void TicketAndCreateDto_DefaultToMedium()
    {
        Assert.Equal(TicketPriority.Medium, new Ticket().Priority);
        Assert.True(TicketPriorityValidation.TryParse(new CreateTicketDto().Priority, out var priority));
        Assert.Equal(TicketPriority.Medium, priority);
    }

    [Fact]
    public void Model_UsesTextAndMediumSentinelSoLowCanBeInserted()
    {
        // Inspección del modelo EF, sin abrir una conexión.
        var options = new DbContextOptionsBuilder<HelpDeskDbContext>()
            .UseNpgsql("Host=localhost;Database=helpdesk_test_db").Options;
        using var db = new HelpDeskDbContext(options);
        var property = db.Model.FindEntityType(typeof(Ticket))!.FindProperty(nameof(Ticket.Priority))!;

        Assert.Equal(TicketPriority.Medium, property.GetDefaultValue());
        Assert.Equal(TicketPriority.Medium, property.Sentinel);
        var converter = property.GetTypeMapping().Converter!;
        Assert.Equal("Low", converter.ConvertToProvider(TicketPriority.Low));
        Assert.Equal("Medium", converter.ConvertToProvider(TicketPriority.Medium));
    }
}
