namespace HelpDesk.Api.Validation;

public static class TicketCommentValidation
{
    public static string? Validate(string? content) => string.IsNullOrWhiteSpace(content)
        ? "El comentario es obligatorio y no puede contener solamente espacios."
        : content.Length > 4000 ? "El comentario debe tener como máximo 4000 unidades UTF-16."
        : content.Contains('\0') ? "El comentario no puede contener caracteres nulos."
        : null;
}
