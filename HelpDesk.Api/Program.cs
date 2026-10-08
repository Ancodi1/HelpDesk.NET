using HelpDesk.Api.Data;
using HelpDesk.Api.DTOs;
using HelpDesk.Api.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<HelpDeskDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("Configura ConnectionStrings:DefaultConnection mediante secretos de desarrollo o variables de entorno.");
    }

    options.UseNpgsql(connectionString);
});

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

app.MapGet("/api/tickets", async (HelpDeskDbContext dbContext) =>
{
    var tickets = await dbContext.Tickets.ToListAsync();

    return Results.Ok(tickets);
});

app.MapGet("/api/tickets/{id}", async (int id, HelpDeskDbContext dbContext) =>
{
    var ticket = await dbContext.Tickets.FirstOrDefaultAsync(ticket => ticket.Id == id);

    if (ticket is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(ticket);
});

app.MapPost("/api/tickets", (CreateTicketDto createTicketDto) =>
{
    var error = ValidateTicket(createTicketDto.Title, createTicketDto.Description);

    if (error is not null)
    {
        return Results.BadRequest(new { error });
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

app.MapPut("/api/tickets/{id}", (int id, UpdateTicketDto updateTicketDto) =>
{
    var ticket = tickets.FirstOrDefault(ticket => ticket.Id == id);

    if (ticket is null)
    {
        return Results.NotFound();
    }

    var error = ValidateTicket(updateTicketDto.Title, updateTicketDto.Description);

    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    ticket.Title = updateTicketDto.Title;
    ticket.Description = updateTicketDto.Description;

    return Results.Ok(ticket);
});

app.MapDelete("/api/tickets/{id}", (int id) =>
{
    var ticket = tickets.FirstOrDefault(ticket => ticket.Id == id);

    if (ticket is null)
    {
        return Results.NotFound();
    }

    tickets.Remove(ticket);

    return Results.NoContent();
});

app.Run();

static string? ValidateTicket(string? title, string? description)
{
    if (string.IsNullOrWhiteSpace(title))
    {
        return "El título es obligatorio y no puede contener solamente espacios.";
    }

    if (title.Length > 100)
    {
        return "El título debe tener como máximo 100 caracteres.";
    }

    if (string.IsNullOrWhiteSpace(description))
    {
        return "La descripción es obligatoria y no puede contener solamente espacios.";
    }

    return null;
}
