using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HelpDesk.Api.Authentication;

// No usar un record: su ToString generado podría exponer la clave.
public sealed class JwtSettings
{
    public string? Issuer { get; set; }
    public string? Audience { get; set; }
    public string? SigningKeyBase64 { get; set; }
    public const int LifetimeSeconds = 30 * 60;
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    public TokenValidationParameters CreateValidationParameters() => new()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(SigningKeyBase64!)),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        RequireSignedTokens = true,
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        RequireExpirationTime = true,
        ValidateLifetime = true,
        ClockSkew = ClockSkew,
        NameClaimType = "sub",
        RoleClaimType = "role"
    };
}

public sealed class JwtSettingsValidator : IValidateOptions<JwtSettings>
{
    public ValidateOptionsResult Validate(string? name, JwtSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Issuer) || settings.Issuer != settings.Issuer.Trim() ||
            string.IsNullOrWhiteSpace(settings.Audience) || settings.Audience != settings.Audience.Trim())
            return ValidateOptionsResult.Fail("Configura Jwt:Issuer y Jwt:Audience sin espacios extremos.");

        byte[] key;
        try { key = Convert.FromBase64String(settings.SigningKeyBase64 ?? ""); }
        catch (FormatException) { return InvalidKey(); }
        // Rechaza claves cortas y patrones triviales; la entropía real depende de su generación.
        if (key.Length < 32 || key.Distinct().Count() < 16)
            return InvalidKey();
        return ValidateOptionsResult.Success;
    }

    private static ValidateOptionsResult InvalidKey() => ValidateOptionsResult.Fail(
        "Configura Jwt:SigningKeyBase64 fuera del repositorio con al menos 32 bytes generados criptográficamente y codificados en Base64.");
}
