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

app.MapGet("/api/tickets/{id}", (int id) =>
{
    var ticket = tickets.FirstOrDefault(ticket => ticket.Id == id);

    if (ticket is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(ticket);
});

app.Run();
