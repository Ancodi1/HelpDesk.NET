namespace HelpDesk.Api.DTOs;

public record TicketCommentDto(int Id, int TicketId, int AuthorUserId, string AuthorName, string Content, DateTime CreatedAt);
public record TicketCommentPageDto(IReadOnlyList<TicketCommentDto> Items, bool HasMore, int? NextBeforeId, int PageSize);
public class CreateTicketCommentDto
{
    public string? Content { get; set; }
}
