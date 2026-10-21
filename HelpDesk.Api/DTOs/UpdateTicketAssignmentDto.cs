using System.Text.Json.Serialization;

namespace HelpDesk.Api.DTOs;

public class UpdateTicketAssignmentDto
{
    // null explícito desasigna; omitir el campo es un cuerpo incompleto.
    [JsonRequired]
    public int? AssignedToUserId { get; set; }
}
