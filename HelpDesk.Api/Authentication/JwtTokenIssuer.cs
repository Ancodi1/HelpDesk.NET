using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using HelpDesk.Api.DTOs;
using HelpDesk.Api.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HelpDesk.Api.Authentication;

public sealed class JwtTokenIssuer(IOptions<JwtSettings> options, TimeProvider clock)
{
    public LoginResponseDto Issue(User user)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var claims = new[]
        {
            new Claim("sub", user.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim("email", user.Email),
            new Claim("role", user.Role.ToString()),
            new Claim("iat", now.ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64)
        };
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience, claims,
            notBefore: DateTimeOffset.FromUnixTimeSeconds(now).UtcDateTime,
            expires: DateTimeOffset.FromUnixTimeSeconds(now + JwtSettings.LifetimeSeconds).UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Convert.FromBase64String(settings.SigningKeyBase64!)), SecurityAlgorithms.HmacSha256));
        return new LoginResponseDto(new JwtSecurityTokenHandler().WriteToken(token), "Bearer", JwtSettings.LifetimeSeconds);
    }
}
