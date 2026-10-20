using HelpDesk.Api.Models;

namespace HelpDesk.Api.DTOs;

public class CreateTicketDto
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Priority { get; set; } = nameof(TicketPriority.Medium);
}
