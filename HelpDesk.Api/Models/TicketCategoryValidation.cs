namespace HelpDesk.Api.Models;

public static class TicketCategoryValidation
{
    public static bool TryParse(string? value, out TicketCategory category)
    {
        category = TicketCategory.Other;
        if (value is null || !Enum.IsDefined(typeof(TicketCategory), value))
        {
            return false;
        }

        category = Enum.Parse<TicketCategory>(value);
        return true;
    }
}
