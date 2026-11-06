using HelpDesk.Api.Validation;

namespace HelpDesk.Api.Demo;

// Clase deliberadamente sin ToString generado: contiene configuración sensible.
public sealed class DemoSettings
{
    public bool Enabled { get; set; }
    public string? ConnectionString { get; set; }
    public string? InstanceId { get; set; }
    public string? UserPassword { get; set; }
    public string? TechnicianPassword { get; set; }
    public const int MaxTickets = 100;
    public const int MaxComments = 500;
    public const int MaxActivities = 1000;

    public void Validate()
    {
        if (!Enabled) return;
        _ = new DemoTarget(ConnectionString, InstanceId);
        if (PasswordValidation.Validate(UserPassword) is not null ||
            PasswordValidation.Validate(TechnicianPassword) is not null || UserPassword == TechnicianPassword)
            throw new InvalidOperationException("Configura dos contraseñas demo distintas con la política de registro, fuera del repositorio.");
    }
}

public static class DemoAccounts
{
    public const int UserId = 1;
    public const int TechnicianId = 2;
    public const int AdminId = 3;
    public const string UserEmail = "user@demo.helpdesk.test";
    public const string TechnicianEmail = "technician@demo.helpdesk.test";
    public const string AdminEmail = "owner@demo.helpdesk.test";
    public static bool IsProtected(int id) => id is UserId or TechnicianId or AdminId;
}
