using HelpDesk.Api.Models;

namespace HelpDesk.Api.DTOs;

public record TicketPageDto(IReadOnlyList<Ticket> Items, int TotalCount, int Page, int PageSize, int TotalPages);
