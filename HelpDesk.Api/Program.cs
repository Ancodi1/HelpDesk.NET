using HelpDesk.Api.Data;
using HelpDesk.Api.DTOs;
using HelpDesk.Api.Models;
using HelpDesk.Api.Validation;
using Npgsql;
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

app.MapGet("/api/tickets", async (HttpRequest request, HelpDeskDbContext dbContext) =>
{
    var parameters = request.Query;
    // Validar antes de ejecutar SQL; rechazar parámetros conocidos repetidos evita ambigüedad.
    foreach (var name in new[] { "status", "priority", "category", "search", "page", "pageSize" })
    {
        if (parameters[name].Count > 1)
        {
            return Results.BadRequest(new { error = $"El parámetro {name} no puede repetirse." });
        }
    }

    IQueryable<Ticket> query = dbContext.Tickets.AsNoTracking();
    if (parameters.TryGetValue("status", out var statusText))
    {
        if (!Enum.IsDefined(typeof(TicketStatus), statusText.ToString()))
            return Results.BadRequest(new { error = "status debe ser Open, InProgress, Resolved o Closed." });
        var status = Enum.Parse<TicketStatus>(statusText.ToString());
        query = query.Where(ticket => ticket.Status == status);
    }
    if (parameters.TryGetValue("priority", out var priorityText))
    {
        if (!TicketPriorityValidation.TryParse(priorityText.ToString(), out var priority))
            return Results.BadRequest(new { error = "priority debe ser Low, Medium, High o Critical." });
        query = query.Where(ticket => ticket.Priority == priority);
    }
    if (parameters.TryGetValue("category", out var categoryText))
    {
        if (!TicketCategoryValidation.TryParse(categoryText.ToString(), out var category))
            return Results.BadRequest(new { error = "category debe ser Hardware, Software, Network, Access u Other." });
        query = query.Where(ticket => ticket.Category == category);
    }

    var page = 1;
    var pageSize = 10;
    if (parameters.TryGetValue("page", out var pageText) &&
        (!int.TryParse(pageText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out page) || page < 1))
        return Results.BadRequest(new { error = "page debe ser un entero positivo." });
    if (parameters.TryGetValue("pageSize", out var sizeText) &&
        (!int.TryParse(sizeText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out pageSize) || pageSize < 1 || pageSize > 100))
        return Results.BadRequest(new { error = "pageSize debe ser un entero entre 1 y 100." });

    var offset = ((long)page - 1) * pageSize;
    if (offset > int.MaxValue)
        return Results.BadRequest(new { error = "La combinación de page y pageSize supera el desplazamiento máximo permitido." });

    var search = parameters["search"].ToString();
    if (search.Length > 0)
    {
        // ILIKE ignora mayúsculas; escapar sus comodines conserva una búsqueda literal.
        var pattern = "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        query = query.Where(ticket => EF.Functions.ILike(ticket.Title, pattern, "\\") ||
                                      EF.Functions.ILike(ticket.Description, pattern, "\\"));
    }

    var totalCount = await query.CountAsync();
    var items = await query.OrderByDescending(ticket => ticket.CreatedAt)
        .ThenByDescending(ticket => ticket.Id)
        .Skip((int)offset).Take(pageSize).ToListAsync();
    var totalPages = (int)((totalCount + (long)pageSize - 1) / pageSize);

    return Results.Ok(new TicketPageDto(items, totalCount, page, pageSize, totalPages));
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

app.MapGet("/api/users", async (HelpDeskDbContext dbContext) =>
{
    var users = await dbContext.Users.AsNoTracking().OrderBy(user => user.Id)
        .Select(user => new UserResponseDto(user.Id, user.Name, user.Email, user.Role, user.CreatedAt))
        .ToListAsync();
    return Results.Ok(users);
});

app.MapGet("/api/users/{id}", async (int id, HelpDeskDbContext dbContext) =>
{
    var user = await dbContext.Users.AsNoTracking().Where(user => user.Id == id)
        .Select(user => new UserResponseDto(user.Id, user.Name, user.Email, user.Role, user.CreatedAt))
        .FirstOrDefaultAsync();
    if (user is null)
        return Results.NotFound();
    return Results.Ok(user);
});

app.MapPost("/api/users", async (CreateUserDto dto, HelpDeskDbContext dbContext) =>
{
    var error = UserValidation.Validate(dto.Name, dto.Email, dto.Role, out var email, out var role);
    if (error is not null)
        return Results.BadRequest(new { error });

    if (await dbContext.Users.AnyAsync(user => user.Email == email))
        return Results.Conflict(new { error = "Ya existe un usuario con ese correo electrónico." });

    var user = new User { Name = dto.Name!, Email = email, Role = role, CreatedAt = DateTime.UtcNow };
    dbContext.Users.Add(user);
    try
    {
        await dbContext.SaveChangesAsync();
    }
    catch (DbUpdateException exception) when (IsDuplicateUserEmail(exception))
    {
        // El índice sigue siendo la autoridad si dos solicitudes superan la consulta previa.
        return Results.Conflict(new { error = "Ya existe un usuario con ese correo electrónico." });
    }
    return Results.Created($"/api/users/{user.Id}", ToUserResponse(user));
});

app.MapPut("/api/users/{id}", async (int id, UpdateUserDto dto, HelpDeskDbContext dbContext) =>
{
    var user = await dbContext.Users.FirstOrDefaultAsync(user => user.Id == id);
    if (user is null)
        return Results.NotFound();

    var error = UserValidation.Validate(dto.Name, dto.Email, dto.Role, out var email, out var role);
    if (error is not null)
        return Results.BadRequest(new { error });

    if (await dbContext.Users.AnyAsync(other => other.Email == email && other.Id != id))
        return Results.Conflict(new { error = "Ya existe un usuario con ese correo electrónico." });

    user.Name = dto.Name!;
    user.Email = email;
    user.Role = role;
    try
    {
        await dbContext.SaveChangesAsync();
    }
    catch (DbUpdateException exception) when (IsDuplicateUserEmail(exception))
    {
        return Results.Conflict(new { error = "Ya existe un usuario con ese correo electrónico." });
    }
    return Results.Ok(ToUserResponse(user));
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

static UserResponseDto ToUserResponse(User user) => new(user.Id, user.Name, user.Email, user.Role, user.CreatedAt);

static bool IsDuplicateUserEmail(DbUpdateException exception) => exception.InnerException is PostgresException
{
    SqlState: PostgresErrorCodes.UniqueViolation,
    ConstraintName: "IX_Users_Email"
};

public partial class Program { }
