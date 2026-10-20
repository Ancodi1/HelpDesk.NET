using HelpDesk.Api.Data;
using HelpDesk.Api.DTOs;
using HelpDesk.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RouteHandlerOptions>(options =>
{
    options.ThrowOnBadRequest = false;
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

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

app.UseStatusCodePages(async context =>
{
    if (context.HttpContext.Response.StatusCode == StatusCodes.Status400BadRequest)
    {
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "El cuerpo JSON de la petición no es válido."
        });
    }
});

app.MapGet("/api/health", () => new { status = "ok" });

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

app.MapPost("/api/tickets", async (CreateTicketDto createTicketDto, HelpDeskDbContext dbContext) =>
{
    var error = ValidateTicket(createTicketDto.Title, createTicketDto.Description);

    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    var ticket = new Ticket
    {
        Title = createTicketDto.Title,
        Description = createTicketDto.Description,
        CreatedAt = DateTime.UtcNow
    };

    dbContext.Tickets.Add(ticket);
    await dbContext.SaveChangesAsync();

    return Results.Created($"/api/tickets/{ticket.Id}", ticket);
});

app.MapPut("/api/tickets/{id}", async (int id, UpdateTicketDto updateTicketDto, HelpDeskDbContext dbContext) =>
{
    var ticket = await dbContext.Tickets.FirstOrDefaultAsync(ticket => ticket.Id == id);

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

    await dbContext.SaveChangesAsync();

    return Results.Ok(ticket);
});

app.MapPatch("/api/tickets/{id}/status", async (int id, UpdateTicketStatusDto updateTicketStatusDto, HelpDeskDbContext dbContext) =>
{
    var ticket = await dbContext.Tickets.FirstOrDefaultAsync(ticket => ticket.Id == id);

    if (ticket is null)
    {
        return Results.NotFound();
    }

    if (updateTicketStatusDto.Status is null || !Enum.IsDefined(typeof(TicketStatus), updateTicketStatusDto.Status))
    {
        return Results.BadRequest(new { error = "El estado debe ser Open, InProgress, Resolved o Closed." });
    }

    ticket.Status = Enum.Parse<TicketStatus>(updateTicketStatusDto.Status);
    await dbContext.SaveChangesAsync();

    return Results.Ok(ticket);
});

app.MapDelete("/api/tickets/{id}", async (int id, HelpDeskDbContext dbContext) =>
{
    var ticket = await dbContext.Tickets.FirstOrDefaultAsync(ticket => ticket.Id == id);

    if (ticket is null)
    {
        return Results.NotFound();
    }

    dbContext.Tickets.Remove(ticket);
    await dbContext.SaveChangesAsync();

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
