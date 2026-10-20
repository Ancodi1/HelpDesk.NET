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

    if (!TicketPriorityValidation.TryParse(createTicketDto.Priority, out var priority))
    {
        return Results.BadRequest(new { error = "La prioridad debe ser Low, Medium, High o Critical." });
    }

    if (!TicketCategoryValidation.TryParse(createTicketDto.Category, out var category))
    {
        return Results.BadRequest(new { error = "La categoría debe ser Hardware, Software, Network, Access u Other." });
    }

    var ticket = new Ticket
    {
        Title = createTicketDto.Title,
        Description = createTicketDto.Description,
        CreatedAt = DateTime.UtcNow,
        Priority = priority,
        Category = category
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

    var requestedStatus = Enum.Parse<TicketStatus>(updateTicketStatusDto.Status);

    if (ticket.Status == requestedStatus)
    {
        return Results.Ok(ticket);
    }

    if (!TicketStatusTransitions.IsAllowed(ticket.Status, requestedStatus))
    {
        return Results.Conflict(new
        {
            error = $"No se permite cambiar el estado de {ticket.Status} a {requestedStatus}.",
            currentStatus = ticket.Status,
            requestedStatus
        });
    }

    ticket.Status = requestedStatus;
    await dbContext.SaveChangesAsync();

    return Results.Ok(ticket);
});

app.MapPatch("/api/tickets/{id}/priority", async (int id, UpdateTicketPriorityDto updateTicketPriorityDto, HelpDeskDbContext dbContext) =>
{
    var ticket = await dbContext.Tickets.FirstOrDefaultAsync(ticket => ticket.Id == id);

    if (ticket is null)
    {
        return Results.NotFound();
    }

    if (!TicketPriorityValidation.TryParse(updateTicketPriorityDto.Priority, out var priority))
    {
        return Results.BadRequest(new { error = "La prioridad debe ser Low, Medium, High o Critical." });
    }

    if (ticket.Priority == priority)
    {
        return Results.Ok(ticket);
    }

    ticket.Priority = priority;
    await dbContext.SaveChangesAsync();

    return Results.Ok(ticket);
});

app.MapPatch("/api/tickets/{id}/category", async (int id, UpdateTicketCategoryDto updateTicketCategoryDto, HelpDeskDbContext dbContext) =>
{
    var ticket = await dbContext.Tickets.FirstOrDefaultAsync(ticket => ticket.Id == id);

    if (ticket is null)
    {
        return Results.NotFound();
    }

    if (!TicketCategoryValidation.TryParse(updateTicketCategoryDto.Category, out var category))
    {
        return Results.BadRequest(new { error = "La categoría debe ser Hardware, Software, Network, Access u Other." });
    }

    if (ticket.Category == category)
    {
        return Results.Ok(ticket);
    }

    ticket.Category = category;
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

public partial class Program { }
