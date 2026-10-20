using HelpDesk.Api.Data;
using HelpDesk.Api.DTOs;
using HelpDesk.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Tests;

public class TicketCategoryValidationTests
{
    [Theory]
    [InlineData("Hardware", TicketCategory.Hardware)]
    [InlineData("Other", TicketCategory.Other)]
    [InlineData("Software", TicketCategory.Software)]
    [InlineData("Network", TicketCategory.Network)]
    [InlineData("Access", TicketCategory.Access)]
    public void TryParse_DefinedName_ReturnsCategory(string value, TicketCategory expected)
    {
        Assert.True(TicketCategoryValidation.TryParse(value, out var category));
        Assert.Equal(expected, category);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("hardware")]
    [InlineData("1")]
    [InlineData("-1")]
    [InlineData("4")]
    [InlineData("Network, Access")]
    [InlineData("Inventado")]
    public void TryParse_InvalidValue_ReturnsFalse(string? value)
    {
        Assert.False(TicketCategoryValidation.TryParse(value, out _));
    }

    [Fact]
    public void TicketAndCreateDto_DefaultToOther()
    {
        Assert.Equal(TicketCategory.Other, new Ticket().Category);
        Assert.True(TicketCategoryValidation.TryParse(new CreateTicketDto().Category, out var category));
        Assert.Equal(TicketCategory.Other, category);
    }

    [Fact]
    public void Model_UsesTextAndOtherSentinelSoHardwareCanBeInserted()
    {
        // Inspección del modelo EF, sin abrir una conexión.
        var options = new DbContextOptionsBuilder<HelpDeskDbContext>()
            .UseNpgsql("Host=localhost;Database=helpdesk_test_db").Options;
        using var db = new HelpDeskDbContext(options);
        var property = db.Model.FindEntityType(typeof(Ticket))!.FindProperty(nameof(Ticket.Category))!;

        Assert.Equal(TicketCategory.Other, property.GetDefaultValue());
        Assert.Equal(TicketCategory.Other, property.Sentinel);
        var converter = property.GetTypeMapping().Converter!;
        Assert.Equal("Hardware", converter.ConvertToProvider(TicketCategory.Hardware));
        Assert.Equal("Other", converter.ConvertToProvider(TicketCategory.Other));
    }
}
