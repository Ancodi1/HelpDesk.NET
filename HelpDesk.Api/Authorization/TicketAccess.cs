using HelpDesk.Api.Models;

namespace HelpDesk.Api.Authorization;

public static class TicketAccess
{
    // Se compone en SQL antes de Count/Skip/Take y se reutiliza para el detalle.
    public static IQueryable<Ticket> VisibleTo(this IQueryable<Ticket> tickets, CurrentAccount account) =>
        account.Role switch
        {
            UserRole.Admin => tickets,
            UserRole.Technician => tickets.Where(ticket => ticket.CreatedByUserId == account.Id ||
                                                           ticket.AssignedToUserId == account.Id),
            UserRole.User => tickets.Where(ticket => ticket.CreatedByUserId == account.Id),
            _ => tickets.Where(ticket => false)
        };

    public static bool CanChangeStatus(Ticket ticket, CurrentAccount account) =>
        account.Role == UserRole.Admin ||
        (account.Role == UserRole.Technician && ticket.AssignedToUserId == account.Id);
}
