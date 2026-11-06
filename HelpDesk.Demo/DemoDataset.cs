using HelpDesk.Api.Data;
using HelpDesk.Api.Demo;
using HelpDesk.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Demo;

public static class DemoDataset
{
    public static User[] Users() =>
    [
        Account(1, "Visitante de ejemplo", DemoAccounts.UserEmail, UserRole.User),
        Account(2, "Técnica de ejemplo", DemoAccounts.TechnicianEmail, UserRole.Technician),
        Account(3, "Administración privada", DemoAccounts.AdminEmail, UserRole.Admin),
        Account(4, "Equipo Aurora (ficticio)", "aurora@demo.helpdesk.test", UserRole.User),
        Account(5, "Soporte Atlas (ficticio)", "atlas@demo.helpdesk.test", UserRole.Technician)
    ];

    private static User Account(int id, string name, string email, UserRole role) =>
        new() { Id = id, Name = name, Email = email, Role = role, CreatedAt = Start };
    public static readonly DateTime Start = new(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc);

    public static async Task PopulateAsync(HelpDeskDbContext db, CancellationToken cancellation = default)
    {
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, cancellation);
        var rows = new[]
        {
            ("Impresora de la sala Norte sin conexión", "El equipo de ejemplo no puede imprimir la guía de bienvenida.", TicketCategory.Hardware, TicketPriority.Medium, TicketStatus.Open, 1, (int?)2),
            ("VPN intermitente en el entorno de formación", "La conexión de laboratorio se interrumpe al cambiar de red.", TicketCategory.Network, TicketPriority.High, TicketStatus.InProgress, 1, (int?)2),
            ("Error al exportar el informe de inventario", "El informe ficticio omite la última columna. Se dispone de un caso reproducible.", TicketCategory.Software, TicketPriority.High, TicketStatus.Resolved, 1, (int?)2),
            ("Acceso al aula virtual de pruebas", "Se solicita acceso al material de demostración del equipo Aurora.", TicketCategory.Access, TicketPriority.Low, TicketStatus.Closed, 1, (int?)2),
            ("Proyector de laboratorio con imagen tenue", "La imagen pierde contraste después de unos minutos de uso.", TicketCategory.Hardware, TicketPriority.Low, TicketStatus.Open, 1, (int?)null),
            ("Revisar el catálogo de servicios de ejemplo", "La ficha de soporte muestra una ruta de atención desactualizada.", TicketCategory.Other, TicketPriority.Critical, TicketStatus.Open, 1, (int?)null),
            ("Copia de seguridad del simulador incompleta", "El simulador detiene la copia al llegar al segundo conjunto ficticio.", TicketCategory.Software, TicketPriority.Critical, TicketStatus.InProgress, 4, (int?)5),
            ("Actualizar la guía de periféricos", "La documentación de laboratorio debe reflejar el nuevo material simulado.", TicketCategory.Other, TicketPriority.Medium, TicketStatus.Resolved, 2, (int?)5)
        };
        foreach (var (title, description, category, priority, status, creator, assignee) in rows)
        {
            var ticket = new Ticket { Title = title, Description = description + " Datos totalmente ficticios.",
                CreatedByUserId = creator, CreatedAt = Start };
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync(cancellation);
            var instant = Start.AddMinutes(10);
            void Activity(string field, string? oldValue, string? newValue, string? newName = null)
            {
                db.TicketActivities.Add(new TicketActivity
                {
                    TicketId = ticket.Id, ActorUserId = 3, ActorNameSnapshot = users[3].Name,
                    Field = field, OldValue = oldValue, NewValue = newValue,
                    NewAssigneeNameSnapshot = newName, CreatedAt = instant
                });
                instant = instant.AddMinutes(5);
            }
            if (assignee is int assigned)
                Activity("AssignedToUserId", null, assigned.ToString(System.Globalization.CultureInfo.InvariantCulture), users[assigned].Name);
            if (category != TicketCategory.Other) Activity("Category", "Other", category.ToString());
            ticket.Category = category;
            if (priority != TicketPriority.Medium) Activity("Priority", "Medium", priority.ToString());
            foreach (var step in new[] { TicketStatus.InProgress, TicketStatus.Resolved, TicketStatus.Closed })
            {
                if (status == TicketStatus.Open) break;
                Activity("Status", ticket.Status.ToString(), step.ToString());
                ticket.Status = step;
                if (step == status) break;
            }
            ticket.AssignedToUserId = assignee;
            ticket.Priority = priority;
            db.TicketComments.Add(new TicketComment
            {
                TicketId = ticket.Id, AuthorUserId = creator, AuthorNameSnapshot = users[creator].Name,
                Content = "Caso de demostración reproducido con datos simulados. No incluye información de personas reales.", CreatedAt = Start.AddMinutes(2)
            });
            if (assignee is int author)
                db.TicketComments.Add(new TicketComment { TicketId = ticket.Id, AuthorUserId = author,
                    AuthorNameSnapshot = users[author].Name, Content = "Revisión realizada en el laboratorio ficticio. El estado refleja el avance de este ejemplo.", CreatedAt = instant });
            await db.SaveChangesAsync(cancellation);
        }
    }
}
