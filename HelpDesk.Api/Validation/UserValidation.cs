using System.Net.Mail;
using HelpDesk.Api.Models;

namespace HelpDesk.Api.Validation;

public static class UserValidation
{
    public static string? Validate(string? name, string? email, string? roleText,
        out string normalizedEmail, out UserRole role)
    {
        normalizedEmail = email?.Trim().ToLowerInvariant() ?? string.Empty;
        role = UserRole.User;
        if (string.IsNullOrWhiteSpace(name))
            return "El nombre es obligatorio y no puede contener solamente espacios.";
        if (name.Length > 100)
            return "El nombre debe tener como máximo 100 caracteres.";
        if (normalizedEmail.Length == 0)
            return "El correo electrónico es obligatorio.";
        if (normalizedEmail.Length > 254)
            return "El correo electrónico debe tener como máximo 254 caracteres.";

        if (normalizedEmail.Any(char.IsWhiteSpace) ||
            !MailAddress.TryCreate(normalizedEmail, out var address) || address.Address != normalizedEmail)
            return "El formato del correo electrónico no es válido.";

        var labels = address.Host.Split('.');
        if (labels.Length < 2 || labels.Any(label => label.Length is 0 or > 63 ||
            label[0] == '-' || label[^1] == '-' || label.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')))
            return "El correo electrónico debe incluir un dominio válido con al menos un punto.";

        if (roleText is null || !Enum.IsDefined(typeof(UserRole), roleText))
            return "El rol debe ser User, Technician o Admin.";
        role = Enum.Parse<UserRole>(roleText);
        return null;
    }
}
