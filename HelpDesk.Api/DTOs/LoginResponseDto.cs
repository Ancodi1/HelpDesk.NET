namespace HelpDesk.Api.DTOs;

public sealed record LoginResponseDto(string AccessToken, string TokenType, int ExpiresIn);
