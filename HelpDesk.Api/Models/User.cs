namespace HelpDesk.Api.Models;

public class User
{
    private string email = string.Empty;

    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email
    {
        get => email;
        // Canonizar también al actualizar; NOT NULL se exige en PostgreSQL.
        set => email = value is null ? null! : value.Trim().ToLowerInvariant();
    }
    public UserRole Role { get; set; } = UserRole.User;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
