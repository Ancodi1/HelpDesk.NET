using HelpDesk.Api.Models;

namespace HelpDesk.Tests;

public class TicketStatusTransitionsTests
{
    [Theory]
    [InlineData(TicketStatus.Open, TicketStatus.InProgress)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Resolved)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Open)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Closed)]
    [InlineData(TicketStatus.Resolved, TicketStatus.InProgress)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Open)]
    [InlineData(TicketStatus.Closed, TicketStatus.Open)]
    public void IsAllowed_AllowedTransition_ReturnsTrue(TicketStatus currentStatus, TicketStatus requestedStatus)
    {
        var result = TicketStatusTransitions.IsAllowed(currentStatus, requestedStatus);

        Assert.True(result);
    }

    [Theory]
    [InlineData(TicketStatus.Open, TicketStatus.Resolved)]
    [InlineData(TicketStatus.Open, TicketStatus.Closed)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Closed)]
    [InlineData(TicketStatus.Closed, TicketStatus.InProgress)]
    [InlineData(TicketStatus.Closed, TicketStatus.Resolved)]
    public void IsAllowed_ForbiddenTransition_ReturnsFalse(TicketStatus currentStatus, TicketStatus requestedStatus)
    {
        var result = TicketStatusTransitions.IsAllowed(currentStatus, requestedStatus);

        Assert.False(result);
    }

    [Theory]
    [InlineData(TicketStatus.Open)]
    [InlineData(TicketStatus.InProgress)]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public void IsAllowed_SameStatus_ReturnsFalse(TicketStatus status)
    {
        // El endpoint resuelve la igualdad con HTTP 200 antes de consultar esta función.
        var result = TicketStatusTransitions.IsAllowed(status, status);

        Assert.False(result);
    }
}
