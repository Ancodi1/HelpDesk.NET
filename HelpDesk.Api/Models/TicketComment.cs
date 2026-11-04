namespace HelpDesk.Api.Models;

public class TicketComment
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public int AuthorUserId { get; set; }
    public string AuthorNameSnapshot { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
