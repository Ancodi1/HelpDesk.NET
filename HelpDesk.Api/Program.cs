using HelpDesk.Api.DTOs;
using HelpDesk.Api.Models;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/api/health", () => new { status = "ok" });

var tickets = new List<Ticket>
{
    new Ticket
    {
        Id = 1,
        Title = "No puedo acceder al correo",
        Description = "El correo muestra un error al iniciar sesión.",
        CreatedAt = new DateTime(2026, 10, 6, 10, 0, 0, DateTimeKind.Utc)
    },
    new Ticket
    {
        Id = 2,
        Title = "La impresora no imprime",
        Description = "Los documentos permanecen en la cola de impresión.",
        CreatedAt = new DateTime(2026, 10, 6, 11, 0, 0, DateTimeKind.Utc)
    },
    new Ticket
    {
        Id = 3,
        Title = "No tengo conexión a Internet",
        Description = "El equipo no puede acceder a páginas web.",
        CreatedAt = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc)
    }
};

app.MapGet("/api/tickets", () => Results.Ok(tickets));

app.MapGet("/api/tickets/{id}", (int id) =>
{
    var ticket = tickets.FirstOrDefault(ticket => ticket.Id == id);

    if (ticket is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(ticket);
});

app.MapPost("/api/tickets", (CreateTicketDto createTicketDto) =>
{
    if (string.IsNullOrWhiteSpace(createTicketDto.Title))
    {
        return Results.BadRequest(new { error = "El título es obligatorio y no puede contener solamente espacios." });
    }

    if (createTicketDto.Title.Length > 100)
    {
        return Results.BadRequest(new { error = "El título debe tener como máximo 100 caracteres." });
    }

    if (string.IsNullOrWhiteSpace(createTicketDto.Description))
    {
        return Results.BadRequest(new { error = "La descripción es obligatoria y no puede contener solamente espacios." });
    }

    var ticket = new Ticket
    {
        Id = tickets.Max(ticket => ticket.Id) + 1,
        Title = createTicketDto.Title,
        Description = createTicketDto.Description,
        CreatedAt = DateTime.UtcNow
    };

    tickets.Add(ticket);

    return Results.Created($"/api/tickets/{ticket.Id}", ticket);
});

app.Run();
