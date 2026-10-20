namespace HelpDesk.Api.Models;

public static class TicketStatusTransitions
{
    public static bool IsAllowed(TicketStatus currentStatus, TicketStatus requestedStatus)
    {
        return currentStatus switch
        {
            TicketStatus.Open => requestedStatus is TicketStatus.InProgress,
            TicketStatus.InProgress => requestedStatus is TicketStatus.Resolved or TicketStatus.Open,
            TicketStatus.Resolved => requestedStatus is TicketStatus.Closed or TicketStatus.InProgress or TicketStatus.Open,
            TicketStatus.Closed => requestedStatus is TicketStatus.Open,
            _ => false
        };
    }
}
