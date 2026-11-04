namespace HelpDesk.Api.Models;

public class TicketActivity
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public int ActorUserId { get; set; }
    public string ActorNameSnapshot { get; set; } = string.Empty;
    public string Field { get; set; } = string.Empty;
    // Enum names, or invariant user IDs (null means unassigned).
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? OldAssigneeNameSnapshot { get; set; }
    public string? NewAssigneeNameSnapshot { get; set; }
    public DateTime CreatedAt { get; set; }
}
