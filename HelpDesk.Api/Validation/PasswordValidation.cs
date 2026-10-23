namespace HelpDesk.Api.Validation;

public static class PasswordValidation
{
    public static string? Validate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return "La contraseña es obligatoria y no puede contener solamente espacios.";
        // Medida en unidades UTF-16, igual que string.Length. Sin truncar ni normalizar.
        if (password.Length is < 15 or > 128)
            return "La contraseña debe tener entre 15 y 128 caracteres UTF-16.";
        if (password.Any(char.IsControl))
            return "La contraseña no puede contener caracteres de control.";
        return null;
    }
}
