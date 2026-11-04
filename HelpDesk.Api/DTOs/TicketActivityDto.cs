namespace HelpDesk.Api.DTOs;

public record TicketActivityDto(int Id, int TicketId, int ActorUserId, string ActorName,
    string Field, string? OldValue, string? NewValue, string? OldAssigneeName,
    string? NewAssigneeName, DateTime CreatedAt);
public record TicketActivityPageDto(IReadOnlyList<TicketActivityDto> Items, bool HasMore,
    int? NextBeforeId, int PageSize);
