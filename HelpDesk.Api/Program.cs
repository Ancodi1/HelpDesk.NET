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
using Microsoft.AspNetCore.Identity;
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

// Solo el hasher del framework compartido: no se instala el modelo completo de Identity.
builder.Services.Configure<PasswordHasherOptions>(options =>
{
    options.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3;
    options.IterationCount = 210_000;
});
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

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

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => new { status = "ok" }).AllowAnonymous();

app.MapGet("/api/tickets", async (HttpRequest request, HelpDeskDbContext dbContext, CurrentAccount account) =>
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

    IQueryable<Ticket> query = dbContext.Tickets.AsNoTracking().VisibleTo(account);
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
}).RequireAuthorization();

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

    ticket.Status = requestedStatus;
    await dbContext.SaveChangesAsync();
    await transaction.CommitAsync();

    return Results.Ok(ticket);
}).RequireAuthorization(AccessPolicies.ChangeStatus);

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
}).RequireAuthorization(AccessPolicies.Admin);

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
}).RequireAuthorization(AccessPolicies.Admin);

app.MapPatch("/api/tickets/{id}/assignment", async (int id, UpdateTicketAssignmentDto dto, HelpDeskDbContext dbContext) =>
{
    var ticket = await dbContext.Tickets.FirstOrDefaultAsync(ticket => ticket.Id == id);
    if (ticket is null)
        return Results.NotFound();

    // Desasignar no requiere proteger ningún rol; la escritura de EF ya es atómica.
    await using var transaction = dto.AssignedToUserId.HasValue
        ? await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted)
        : null;
    if (dto.AssignedToUserId is int userId)
    {
        // SHARE permite asignaciones simultáneas, pero impide cambiar el rol hasta el commit.
        var assignee = await dbContext.Users
            .FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {userId} FOR SHARE")
            .AsNoTracking().SingleOrDefaultAsync();
        if (assignee is null)
            return Results.BadRequest(new { error = "El usuario asignado no existe." });
        if (assignee.Role is not (UserRole.Technician or UserRole.Admin))
            return Results.BadRequest(new { error = "Solo se puede asignar un ticket a Technician o Admin." });
    }

    if (ticket.AssignedToUserId == dto.AssignedToUserId)
        return Results.Ok(ticket);

    ticket.AssignedToUserId = dto.AssignedToUserId;
    try
    {
        await dbContext.SaveChangesAsync();
        if (transaction is not null)
            await transaction.CommitAsync();
    }
    catch (DbUpdateException exception) when (IsTicketUserForeignKeyViolation(exception))
    {
        return Results.Conflict(new { error = "El usuario relacionado ya no está disponible. Revisa el ticket y vuelve a intentarlo." });
    }
    return Results.Ok(ticket);
}).RequireAuthorization(AccessPolicies.Admin);

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
    // Mismo protocolo que assignment: usuario antes de cualquier escritura de tickets.
    var user = await dbContext.Users
        .FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {id} FOR NO KEY UPDATE")
        .SingleOrDefaultAsync();
    if (user is null) return Results.NotFound();
    var error = UserValidation.Validate(dto.Name, dto.Email, dto.Role, out var email, out var role);
    if (error is not null) return Results.BadRequest(new { error });
    if (await dbContext.Users.AnyAsync(other => other.Id != id && other.Email == email))
        return Results.Conflict(new { error = "Ya existe un usuario con ese correo electrónico." });
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
}).AllowAnonymous();

app.MapPost("/api/auth/login", async (LoginDto dto, HelpDeskDbContext dbContext,
    IPasswordHasher<User> passwordHasher, JwtTokenIssuer tokens, HttpResponse response) =>
{
    response.Headers.CacheControl = "no-store";
    response.Headers.Pragma = "no-cache";
    if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
        return Results.BadRequest(new { error = "El email y la contraseña son obligatorios." });

    var email = dto.Email.Trim().ToLowerInvariant();
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
}).AllowAnonymous();

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

static bool IsTicketUserForeignKeyViolation(DbUpdateException exception) => exception.InnerException is PostgresException
{
    SqlState: PostgresErrorCodes.ForeignKeyViolation,
    ConstraintName: "FK_Tickets_Users_CreatedByUserId" or "FK_Tickets_Users_AssignedToUserId"
};

public partial class Program { }
