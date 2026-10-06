using HelpDesk.Api.Models;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/api/health", () => new { status = "ok" });

app.MapGet("/api/tickets/{id}", (int id) =>
{
    var ticket = new Ticket
    {
        Id = id,
        Title = "No puedo acceder al correo",
        Description = "El correo muestra un error al iniciar sesión.",
        CreatedAt = new DateTime(2026, 10, 6, 10, 0, 0, DateTimeKind.Utc)
    };

    return ticket;
});

app.Run();
