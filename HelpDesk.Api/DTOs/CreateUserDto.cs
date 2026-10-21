using HelpDesk.Api.Models;

namespace HelpDesk.Api.DTOs;

public class CreateUserDto
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Role { get; set; } = nameof(UserRole.User);
}
