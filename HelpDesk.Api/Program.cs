using HelpDesk.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using HelpDesk.Api.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using HelpDesk.Api.Data;
using HelpDesk.Api.DTOs;
using HelpDesk.Api.Models;
using HelpDesk.Api.Validation;
using Npgsql;
using HelpDesk.Api.Security;
using HelpDesk.Api.Demo;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
HelpDesk.Api.Deployment.ProductionHosting.Configure(builder);

builder.Services.Configure<RouteHandlerOptions>(options =>
{
    options.ThrowOnBadRequest = false;
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddHelpDeskPasswordHashing();
builder.Services.Configure<DemoSettings>(builder.Configuration.GetSection("Demo"));
builder.Services.AddHostedService<DemoStartup>();
builder.Services.AddSingleton<RequestLimits>();
builder.Services.AddRateLimiter();
builder.Services.AddOptions<RateLimiterOptions>().Configure<RequestLimits>(RequestLimits.Configure);

builder.Services.AddSingleton<IValidateOptions<JwtSettings>, JwtSettingsValidator>();
builder.Services.AddOptions<JwtSettings>().BindConfiguration("Jwt").ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<JwtTokenIssuer>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtSettings>>((options, settings) =>
    {
        options.MapInboundClaims = false;
        options.IncludeErrorDetails = false;
        options.SaveToken = false;
        options.TokenValidationParameters = settings.Value.CreateValidationParameters();
    });

builder.Services.AddScoped<CurrentAccount>();
builder.Services.AddScoped<IAuthorizationHandler, CurrentAccountHandler>();
builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = AccessPolicies.AccountPolicy();
    options.FallbackPolicy = options.DefaultPolicy;
    options.AddPolicy(AccessPolicies.Admin, AccessPolicies.AccountPolicy(nameof(UserRole.Admin)));
    options.AddPolicy(AccessPolicies.ChangeStatus,
        AccessPolicies.AccountPolicy(nameof(UserRole.Admin), nameof(UserRole.Technician)));
});

builder.Services.AddDbContext<HelpDeskDbContext>((services, options) =>
{
    var demo = services.GetRequiredService<IOptions<DemoSettings>>().Value;
    demo.Validate();
    var connectionString = demo.Enabled
        ? new DemoTarget(demo.ConnectionString, demo.InstanceId).ConnectionString
        : builder.Configuration.GetConnectionString("DefaultConnection");

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("Configura ConnectionStrings:DefaultConnection mediante secretos de desarrollo o variables de entorno.");
    }

    if (!demo.Enabled)
    {
        var normalTarget = new NpgsqlConnectionStringBuilder(connectionString);
        if (normalTarget.Database == DemoTarget.Database || normalTarget.Username == DemoTarget.Username)
            throw new InvalidOperationException("La base demo solo se puede utilizar con Demo:Enabled y su configuración explícita.");
    }
    options.UseNpgsql(connectionString);
});

var app = builder.Build();

// Evita que la página de excepciones de Development refleje cabeceras o cuerpos sensibles.
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new { error = "No se pudo completar la petición." });
}));

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

HelpDesk.Api.Deployment.ProductionHosting.UseProductionHosting(app);
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseMiddleware<DemoMiddleware>();

app.MapGet("/api/demo", (IOptions<DemoSettings> options, HttpResponse response) =>
{
    response.Headers.CacheControl = "no-store";
    var demo = options.Value;
    return demo.Enabled
        ? Results.Ok((object)new { enabled = true, accounts = new[]
        {
            new { role = "User", email = DemoAccounts.UserEmail, password = demo.UserPassword },
            new { role = "Technician", email = DemoAccounts.TechnicianEmail, password = demo.TechnicianPassword }
        } })
        : Results.Ok((object)new { enabled = false });
}).AllowAnonymous();

app.MapGet("/api/health", () => new { status = "ok" }).AllowAnonymous();

app.MapGet("/api/tickets", async (HttpRequest request, HelpDeskDbContext dbContext, CurrentAccount account) =>
{
    var parameters = request.Query;
    // Validar antes de ejecutar SQL; rechazar parámetros conocidos repetidos evita ambigüedad.
    foreach (var name in new[] { "status", "priority", "category", "search", "page", "pageSize", "assignedToMe" })
    {
        if (parameters[name].Count > 1)
        {
            return Results.BadRequest(new { error = $"El parámetro {name} no puede repetirse." });
        }
    }

    IQueryable<Ticket> query = dbContext.Tickets.AsNoTracking().VisibleTo(account);
    if (parameters.TryGetValue("assignedToMe", out var assignedText))
    {
        if (assignedText.ToString() is not ("true" or "false"))
            return Results.BadRequest(new { error = "assignedToMe debe ser true o false." });
        if (account.Role != UserRole.Technician)
            return Results.Forbid();
        if (assignedText.ToString() == "true")
            query = query.Where(ticket => ticket.AssignedToUserId == account.Id);
    }
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
    if (search.Length > 200 || search.Contains('\0'))
        return Results.BadRequest(new { error = "La búsqueda admite hasta 200 unidades UTF-16 y no admite caracteres nulos." });
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
}).RequireAuthorization();

app.MapGet("/api/tickets/{id}", async (int id, HelpDeskDbContext dbContext, CurrentAccount account) =>
{
    var ticket = await dbContext.Tickets.AsNoTracking().VisibleTo(account).FirstOrDefaultAsync(ticket => ticket.Id == id);

    if (ticket is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(ticket);
}).RequireAuthorization();

app.MapPost("/api/tickets", async (CreateTicketDto createTicketDto, HelpDeskDbContext dbContext, CurrentAccount account) =>
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
        // Se ignora cualquier creador enviado por el cliente, también para Admin.
        CreatedByUserId = account.Id,
        Title = createTicketDto.Title,
        Description = createTicketDto.Description,
        CreatedAt = DateTime.UtcNow,
        Priority = priority,
        Category = category
    };

    dbContext.Tickets.Add(ticket);
    try
    {
        await dbContext.SaveChangesAsync();
    }
    catch (DbUpdateException exception) when (IsTicketUserForeignKeyViolation(exception))
    {
        return Results.Conflict(new { error = "El usuario relacionado ya no está disponible. Revisa el ticket y vuelve a intentarlo." });
    }

    return Results.Created($"/api/tickets/{ticket.Id}", ticket);
}).RequireAuthorization().RequireRateLimiting("ticket-create");

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
}).RequireAuthorization(AccessPolicies.Admin);

app.MapPatch("/api/tickets/{id}/status", async (int id, UpdateTicketStatusDto updateTicketStatusDto, HelpDeskDbContext dbContext, CurrentAccount account) =>
{
    // Bloquear y después comprobar el recurso evita autorizar con una asignación que cambie antes del UPDATE.
    await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
    var ticket = await dbContext.Tickets
        .FromSqlInterpolated($"SELECT * FROM \"Tickets\" WHERE \"Id\" = {id} FOR UPDATE")
        .VisibleTo(account).SingleOrDefaultAsync();

    if (ticket is null)
    {
        return Results.NotFound();
    }

    if (!TicketAccess.CanChangeStatus(ticket, account))
        return Results.Forbid();

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

    await AddActivityAsync(dbContext, account, ticket, "Status", ticket.Status.ToString(), requestedStatus.ToString());
    ticket.Status = requestedStatus;
    await dbContext.SaveChangesAsync();
    await transaction.CommitAsync();

    return Results.Ok(ticket);
}).RequireAuthorization(AccessPolicies.ChangeStatus);

app.MapPatch("/api/tickets/{id}/priority", async (int id, UpdateTicketPriorityDto updateTicketPriorityDto, HelpDeskDbContext dbContext, CurrentAccount account) =>
{
    await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
    var ticket = await dbContext.Tickets.FromSqlInterpolated($"SELECT * FROM \"Tickets\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync();

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

    await AddActivityAsync(dbContext, account, ticket, "Priority", ticket.Priority.ToString(), priority.ToString());
    ticket.Priority = priority;
    await dbContext.SaveChangesAsync();
    await transaction.CommitAsync();

    return Results.Ok(ticket);
}).RequireAuthorization(AccessPolicies.Admin);

app.MapPatch("/api/tickets/{id}/category", async (int id, UpdateTicketCategoryDto updateTicketCategoryDto, HelpDeskDbContext dbContext, CurrentAccount account) =>
{
    await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
    var ticket = await dbContext.Tickets.FromSqlInterpolated($"SELECT * FROM \"Tickets\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync();

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

    await AddActivityAsync(dbContext, account, ticket, "Category", ticket.Category.ToString(), category.ToString());
    ticket.Category = category;
    await dbContext.SaveChangesAsync();
    await transaction.CommitAsync();

    return Results.Ok(ticket);
}).RequireAuthorization(AccessPolicies.Admin);

app.MapPatch("/api/tickets/{id}/assignment", async (int id, UpdateTicketAssignmentDto dto, HelpDeskDbContext dbContext, CurrentAccount account) =>
{
    // Reintentar solo antes de escribir: la asignación puede cambiar mientras se bloquean los usuarios.
    for (var attempt = 0; attempt < 3; attempt++)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
        var observed = await dbContext.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (observed is null) return Results.NotFound();
        var userIds = new[] { observed.AssignedToUserId, dto.AssignedToUserId }
            .Where(value => value.HasValue).Select(value => value!.Value).Distinct().Order().ToArray();
        var users = new Dictionary<int, User>();
        // Bloquear responsable anterior y nuevo por ID, antes del ticket, también al desasignar.
        foreach (var userId in userIds)
        {
            var user = await dbContext.Users
                .FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {userId} FOR SHARE")
                .AsNoTracking().SingleOrDefaultAsync();
            if (user is not null) users.Add(user.Id, user);
        }
        var ticket = await dbContext.Tickets
            .FromSqlInterpolated($"SELECT * FROM \"Tickets\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync();
        if (ticket is null) return Results.NotFound();
        if (ticket.AssignedToUserId != observed.AssignedToUserId)
        {
            await transaction.RollbackAsync();
            dbContext.Entry(ticket).State = EntityState.Detached;
            continue;
        }
        if (dto.AssignedToUserId is int target)
        {
            if (!users.TryGetValue(target, out var assignee))
                return Results.BadRequest(new { error = "El usuario asignado no existe." });
            if (assignee.Role is not (UserRole.Technician or UserRole.Admin))
                return Results.BadRequest(new { error = "Solo se puede asignar un ticket a Technician o Admin." });
        }
        if (ticket.AssignedToUserId == dto.AssignedToUserId) return Results.Ok(ticket);
        await AddActivityAsync(dbContext, account, ticket, "AssignedToUserId",
            ticket.AssignedToUserId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            dto.AssignedToUserId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ticket.AssignedToUserId is int oldId ? users[oldId].Name : null,
            dto.AssignedToUserId is int newId ? users[newId].Name : null);
        ticket.AssignedToUserId = dto.AssignedToUserId;
        try
        {
            await dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException exception) when (IsTicketUserForeignKeyViolation(exception))
        {
            return Results.Conflict(new { error = "El usuario relacionado ya no está disponible. Revisa el ticket y vuelve a intentarlo." });
        }
        return Results.Ok(ticket);
    }
    return Results.Conflict(new { error = "La asignación ha cambiado. Actualiza el ticket antes de continuar." });
}).RequireAuthorization(AccessPolicies.Admin);

app.MapGet("/api/tickets/{id}/activities", async (int id, HttpRequest request, HelpDeskDbContext dbContext, CurrentAccount account) =>
{
    await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
    var ticket = await dbContext.Tickets.FromSqlInterpolated($"SELECT * FROM \"Tickets\" WHERE \"Id\" = {id} FOR SHARE")
        .AsNoTracking().VisibleTo(account).SingleOrDefaultAsync();
    if (ticket is null) return Results.NotFound();
    var parameters = request.Query;
    if (parameters["beforeId"].Count > 1 || parameters["pageSize"].Count > 1)
        return Results.BadRequest(new { error = "Los parámetros de paginación no pueden repetirse." });
    var size = 20;
    if (parameters.TryGetValue("pageSize", out var sizeText) &&
        (!int.TryParse(sizeText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out size) || size is < 1 or > 100))
        return Results.BadRequest(new { error = "pageSize debe ser un entero entre 1 y 100." });
    int? before = null;
    if (parameters.TryGetValue("beforeId", out var beforeText))
    {
        if (!int.TryParse(beforeText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var cursor) || cursor <= 0)
            return Results.BadRequest(new { error = "beforeId debe ser un entero positivo." });
        before = cursor;
    }
    var query = dbContext.TicketActivities.AsNoTracking().Where(c => c.TicketId == id);
    if (before is int beforeId) query = query.Where(c => c.Id < beforeId);
    var items = await query.OrderByDescending(c => c.Id).Take(size + 1)
        .Select(c => new TicketActivityDto(c.Id, c.TicketId, c.ActorUserId, c.ActorNameSnapshot, c.Field, c.OldValue, c.NewValue, c.OldAssigneeNameSnapshot, c.NewAssigneeNameSnapshot, c.CreatedAt)).ToListAsync();
    var more = items.Count > size;
    if (more) items.RemoveAt(items.Count - 1);
    await transaction.CommitAsync();
    return Results.Ok(new TicketActivityPageDto(items, more, more ? items[^1].Id : null, size));
}).RequireAuthorization();

app.MapGet("/api/tickets/{id}/comments", async (int id, HttpRequest request, HelpDeskDbContext dbContext, CurrentAccount account) =>
{
    await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
    var ticket = await dbContext.Tickets.FromSqlInterpolated($"SELECT * FROM \"Tickets\" WHERE \"Id\" = {id} FOR SHARE")
        .AsNoTracking().VisibleTo(account).SingleOrDefaultAsync();
    if (ticket is null) return Results.NotFound();
    var parameters = request.Query;
    if (parameters["beforeId"].Count > 1 || parameters["pageSize"].Count > 1)
        return Results.BadRequest(new { error = "Los parámetros de paginación no pueden repetirse." });
    var size = 20;
    if (parameters.TryGetValue("pageSize", out var sizeText) &&
        (!int.TryParse(sizeText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out size) || size is < 1 or > 100))
        return Results.BadRequest(new { error = "pageSize debe ser un entero entre 1 y 100." });
    int? before = null;
    if (parameters.TryGetValue("beforeId", out var beforeText))
    {
        if (!int.TryParse(beforeText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var cursor) || cursor <= 0)
            return Results.BadRequest(new { error = "beforeId debe ser un entero positivo." });
        before = cursor;
    }
    var query = dbContext.TicketComments.AsNoTracking().Where(c => c.TicketId == id);
    if (before is int beforeId) query = query.Where(c => c.Id < beforeId);
    var items = await query.OrderByDescending(c => c.Id).Take(size + 1)
        .Select(c => new TicketCommentDto(c.Id, c.TicketId, c.AuthorUserId, c.AuthorNameSnapshot, c.Content, c.CreatedAt)).ToListAsync();
    var more = items.Count > size;
    if (more) items.RemoveAt(items.Count - 1);
    await transaction.CommitAsync();
    return Results.Ok(new TicketCommentPageDto(items, more, more ? items[^1].Id : null, size));
}).RequireAuthorization();

app.MapPost("/api/tickets/{id}/comments", async (int id, CreateTicketCommentDto dto, HelpDeskDbContext dbContext, CurrentAccount account) =>
{
    // Serializar publicación, reasignación y borrado antes de comprobar la visibilidad.
    await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
    var ticket = await dbContext.Tickets.FromSqlInterpolated($"SELECT * FROM \"Tickets\" WHERE \"Id\" = {id} FOR UPDATE")
        .VisibleTo(account).SingleOrDefaultAsync();
    if (ticket is null) return Results.NotFound();
    var error = TicketCommentValidation.Validate(dto.Content);
    if (error is not null) return Results.BadRequest(new { error });
    var authorName = await dbContext.Users.Where(u => u.Id == account.Id).Select(u => u.Name).SingleOrDefaultAsync();
    if (authorName is null) return Results.Forbid();
    var comment = new TicketComment
    {
        TicketId = id, AuthorUserId = account.Id, AuthorNameSnapshot = authorName,
        Content = dto.Content!, CreatedAt = DateTime.UtcNow
    };
    dbContext.TicketComments.Add(comment);
    try
    {
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
    }
    catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.ForeignKeyViolation, ConstraintName: "FK_TicketComments_Users_AuthorUserId" })
    {
        return Results.Conflict(new { error = "La cuenta ya no está disponible. Revisa tu sesión antes de continuar." });
    }
    return Results.Json(new TicketCommentDto(comment.Id, id, comment.AuthorUserId, comment.AuthorNameSnapshot, comment.Content, comment.CreatedAt), statusCode: 201);
}).RequireAuthorization().RequireRateLimiting("comment-create");

app.MapDelete("/api/tickets/{id}", async (int id, HelpDeskDbContext dbContext) =>
{
    await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
    var ticket = await dbContext.Tickets.FromSqlInterpolated($"SELECT * FROM \"Tickets\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync();

    if (ticket is null)
    {
        return Results.NotFound();
    }

    if (await dbContext.TicketComments.AnyAsync(c => c.TicketId == id))
        return Results.Conflict(new { error = "No se puede eliminar un ticket con comentarios.", code = "ticket_has_comments" });
    if (await dbContext.TicketActivities.AnyAsync(a => a.TicketId == id))
        return Results.Conflict(new { error = "No se puede eliminar un ticket con historial de actividad.", code = "ticket_has_activities" });
    dbContext.Tickets.Remove(ticket);
    await dbContext.SaveChangesAsync();
    await transaction.CommitAsync();

    return Results.NoContent();
}).RequireAuthorization(AccessPolicies.Admin);

app.MapGet("/api/users", async (HelpDeskDbContext dbContext) =>
{
    var users = await dbContext.Users.AsNoTracking().OrderBy(user => user.Id)
        .Select(user => new UserResponseDto(user.Id, user.Name, user.Email, user.Role, user.CreatedAt))
        .ToListAsync();
    return Results.Ok(users);
}).RequireAuthorization(AccessPolicies.Admin);

app.MapGet("/api/users/{id}", async (int id, HelpDeskDbContext dbContext, CurrentAccount account) =>
{
    if (account.Role != UserRole.Admin && account.Id != id)
        return Results.NotFound();
    var user = await dbContext.Users.AsNoTracking().Where(user => user.Id == id)
        .Select(user => new UserResponseDto(user.Id, user.Name, user.Email, user.Role, user.CreatedAt))
        .FirstOrDefaultAsync();
    if (user is null)
        return Results.NotFound();
    return Results.Ok(user);
}).RequireAuthorization();

app.MapPost("/api/users", async (CreateUserDto dto, HelpDeskDbContext dbContext) =>
{
    var error = UserValidation.Validate(dto.Name, dto.Email, dto.Role, out var email, out var role);
    if (error is not null) return Results.BadRequest(new { error });
    if (await dbContext.Users.AnyAsync(user => user.Email == email))
        return Results.Conflict(new { error = "Ya existe un usuario con ese correo electrónico." });
    // Aprovisionamiento administrativo sin credencial: no habilita acceso ni reclama cuentas existentes.
    var user = new User { Name = dto.Name!, Email = email, Role = role, CreatedAt = DateTime.UtcNow };
    dbContext.Users.Add(user);
    try { await dbContext.SaveChangesAsync(); }
    catch (DbUpdateException exception) when (IsDuplicateUserEmail(exception))
    {
        return Results.Conflict(new { error = "Ya existe un usuario con ese correo electrónico." });
    }
    return Results.Created($"/api/users/{user.Id}", ToUserResponse(user));
}).RequireAuthorization(AccessPolicies.Admin);

app.MapPut("/api/users/{id}", async (int id, UpdateUserDto dto, HelpDeskDbContext dbContext) =>
{
    await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
    // Igual que bootstrap: serializar antes de bloquear filas, también entre instancias de la API.
    // ReadCommitted vuelve a leer los roles confirmados después de esperar al escritor anterior.
    await dbContext.Database.ExecuteSqlRawAsync("LOCK TABLE public.\"Users\" IN SHARE ROW EXCLUSIVE MODE");
    // Mismo protocolo que assignment: usuario antes de cualquier escritura de tickets.
    var user = await dbContext.Users
        .FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {id} FOR NO KEY UPDATE")
        .SingleOrDefaultAsync();
    if (user is null) return Results.NotFound();
    var error = UserValidation.Validate(dto.Name, dto.Email, dto.Role, out var email, out var role);
    if (error is not null) return Results.BadRequest(new { error });
    if (await dbContext.Users.AnyAsync(other => other.Id != id && other.Email == email))
        return Results.Conflict(new { error = "Ya existe un usuario con ese correo electrónico." });
    if (user.Role == UserRole.Admin && role != UserRole.Admin &&
        !await dbContext.Users.AnyAsync(other => other.Id != id && other.Role == UserRole.Admin))
        return Results.Conflict(new
        {
            error = "No se puede cambiar el rol del último administrador.",
            code = "last_admin"
        });
    if (role == UserRole.User && await dbContext.Tickets.AnyAsync(ticket => ticket.AssignedToUserId == id))
        return Results.Conflict(new { error = "No se puede cambiar a User mientras tenga tickets asignados." });
    user.Name = dto.Name!;
    user.Email = email;
    user.Role = role;
    try
    {
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
    }
    catch (DbUpdateException exception) when (IsDuplicateUserEmail(exception))
    {
        return Results.Conflict(new { error = "Ya existe un usuario con ese correo electrónico." });
    }
    return Results.Ok(ToUserResponse(user));
}).RequireAuthorization(AccessPolicies.Admin);

app.MapPost("/api/auth/register", async (RegisterUserDto dto, HelpDeskDbContext dbContext,
    IPasswordHasher<User> passwordHasher) =>
{
    var error = UserValidation.Validate(dto.Name, dto.Email, nameof(UserRole.User), out var email, out _)
        ?? PasswordValidation.Validate(dto.Password);
    if (error is not null)
        return Results.BadRequest(new { error });

    if (await dbContext.Users.AnyAsync(user => user.Email == email))
        return Results.Conflict(new { error = "Ya existe un usuario con ese correo electrónico." });

    var user = new User { Name = dto.Name!, Email = email, Role = UserRole.User, CreatedAt = DateTime.UtcNow };
    user.PasswordHash = passwordHasher.HashPassword(user, dto.Password!);
    dbContext.Users.Add(user);
    try
    {
        await dbContext.SaveChangesAsync();
    }
    catch (DbUpdateException exception) when (IsDuplicateUserEmail(exception))
    {
        // El índice único resuelve también registros simultáneos con el mismo correo.
        return Results.Conflict(new { error = "Ya existe un usuario con ese correo electrónico." });
    }
    return Results.Created($"/api/users/{user.Id}", ToUserResponse(user));
}).AllowAnonymous().RequireRateLimiting("register");

app.MapPost("/api/auth/login", async (LoginDto dto, HelpDeskDbContext dbContext,
    IPasswordHasher<User> passwordHasher, JwtTokenIssuer tokens, HttpResponse response) =>
{
    response.Headers.CacheControl = "no-store";
    response.Headers.Pragma = "no-cache";
    if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
        return Results.BadRequest(new { error = "El email y la contraseña son obligatorios." });

    var email = dto.Email.Trim().ToLowerInvariant();
    if (dto.Password.Length > 128 || dto.Password.Contains('\0') || email.Length > 254 || email.Contains('\0'))
        return Results.BadRequest(new { error = "El email o la contraseña no son válidos." });

    var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Email == email);
    var verified = PasswordVerificationResult.Failed;
    if (!string.IsNullOrEmpty(user?.PasswordHash))
    {
        // Un hash anterior corrupto se trata como credencial inválida, sin revelar su contenido.
        try { verified = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, dto.Password); }
        catch (FormatException) { }
    }
    if (user is null || verified == PasswordVerificationResult.Failed)
        return Results.Json(new { error = "Credenciales incorrectas." }, statusCode: StatusCodes.Status401Unauthorized);

    // SuccessRehashNeeded también acredita la contraseña; no escribimos datos en este login.
    return Results.Ok(tokens.Issue(user));
}).AllowAnonymous().RequireRateLimiting("login");

HelpDesk.Api.Deployment.ProductionHosting.MapProductionEndpoints(app);
app.Run();

// Solo se llama con el ticket bloqueado; SaveChanges y commit guardan cambio y actividad juntos.
static async Task AddActivityAsync(HelpDeskDbContext db, CurrentAccount account, Ticket ticket,
    string field, string? oldValue, string? newValue, string? oldName = null, string? newName = null)
{
    var actorName = await db.Users.Where(u => u.Id == account.Id).Select(u => u.Name).SingleAsync();
    db.TicketActivities.Add(new TicketActivity
    {
        TicketId = ticket.Id, ActorUserId = account.Id, ActorNameSnapshot = actorName,
        Field = field, OldValue = oldValue, NewValue = newValue,
        OldAssigneeNameSnapshot = oldName, NewAssigneeNameSnapshot = newName, CreatedAt = DateTime.UtcNow
    });
}

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

    if (description.Length > 10000)
        return "La descripción debe tener como máximo 10000 unidades UTF-16.";
    if (title.Contains('\0') || description.Contains('\0'))
        return "Los textos del ticket no pueden contener caracteres nulos.";

    return null;
}

static UserResponseDto ToUserResponse(User user) => new(user.Id, user.Name, user.Email, user.Role, user.CreatedAt);

static bool IsDuplicateUserEmail(DbUpdateException exception) => exception.InnerException is PostgresException
{
    SqlState: PostgresErrorCodes.UniqueViolation,
    ConstraintName: "IX_Users_Email"
};

static bool IsTicketUserForeignKeyViolation(DbUpdateException exception) => exception.InnerException is PostgresException
{
    SqlState: PostgresErrorCodes.ForeignKeyViolation,
    ConstraintName: "FK_Tickets_Users_CreatedByUserId" or "FK_Tickets_Users_AssignedToUserId"
};

public partial class Program { }
