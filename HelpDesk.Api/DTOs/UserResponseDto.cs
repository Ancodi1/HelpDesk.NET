using HelpDesk.Api.Models;

namespace HelpDesk.Api.DTOs;

public record UserResponseDto(int Id, string Name, string Email, UserRole Role, DateTime CreatedAt);
