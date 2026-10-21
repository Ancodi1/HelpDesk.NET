using System.Text.Json;
using HelpDesk.Api.Data;
using HelpDesk.Api.DTOs;
using HelpDesk.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Tests;

public class TicketUserRelationTests
{
    [Theory]
    [InlineData(nameof(Ticket.CreatedByUserId), nameof(Ticket.CreatedByUser))]
    [InlineData(nameof(Ticket.AssignedToUserId), nameof(Ticket.AssignedToUser))]
    public void Model_MapsIndependentOptionalRelationsWithRestrictAndIndexes(string property, string navigation)
    {
        using var db = new HelpDeskDbContext(new DbContextOptionsBuilder<HelpDeskDbContext>()
            .UseNpgsql("Host=localhost;Database=helpdesk_test_db").Options);
        var entity = db.Model.FindEntityType(typeof(Ticket))!;
        var fk = Assert.Single(entity.GetForeignKeys(), fk => fk.Properties.Single().Name == property);
        Assert.Equal(typeof(User), fk.PrincipalEntityType.ClrType);
        Assert.False(fk.IsRequired);
        Assert.False(fk.IsUnique);
        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
        Assert.Equal(navigation, fk.DependentToPrincipal!.Name);
        Assert.Null(fk.PrincipalToDependent);
        Assert.True(entity.FindProperty(property)!.IsNullable);
        var index = Assert.Single(entity.GetIndexes(), index => index.Properties.Single().Name == property);
        Assert.False(index.IsUnique);
    }

    [Fact]
    public void LegacyCreation_DefaultsRelationshipsToNullAndDoesNotExposeNavigations()
    {
        var dto = JsonSerializer.Deserialize<CreateTicketDto>("{\"Title\":\"Anterior\",\"Description\":\"Texto\"}")!;
        Assert.Null(dto.CreatedByUserId);
        var ticket = new Ticket { CreatedByUser = new User { Email = "private@example.invalid" }, AssignedToUser = new User() };
        Assert.Null(ticket.CreatedByUserId);
        Assert.Null(ticket.AssignedToUserId);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ticket, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.False(json.RootElement.TryGetProperty("createdByUser", out _));
        Assert.False(json.RootElement.TryGetProperty("assignedToUser", out _));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("createdByUserId").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("assignedToUserId").ValueKind);
    }

    [Theory]
    [InlineData("{\"AssignedToUserId\":null}", null)]
    [InlineData("{\"AssignedToUserId\":5}", 5)]
    public void AssignmentDto_AcceptsExplicitNullOrId(string json, int? expected)
    {
        Assert.Equal(expected, JsonSerializer.Deserialize<UpdateTicketAssignmentDto>(json)!.AssignedToUserId);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"AssignedToUserId\":\"5\"}")]
    [InlineData("{\"AssignedToUserId\":1.5}")]
    public void AssignmentDto_RejectsMissingOrInvalidId(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpdateTicketAssignmentDto>(json));
    }
}
