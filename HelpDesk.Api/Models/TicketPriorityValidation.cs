namespace HelpDesk.Api.Models;

public static class TicketPriorityValidation
{
    public static bool TryParse(string? value, out TicketPriority priority)
    {
        priority = TicketPriority.Medium;
        if (value is null || !Enum.IsDefined(typeof(TicketPriority), value))
        {
            return false;
        }

        priority = Enum.Parse<TicketPriority>(value);
        return true;
    }
}
