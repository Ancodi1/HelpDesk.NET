using HelpDesk.Api.Data;
using HelpDesk.Api.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.Tests.Integration;

public partial class TicketEndpointsTests
{
    [Fact]
    public async Task PasswordMigration_PreservesPreexistingRowsAndForeignKeys()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        // Tablas temporales privadas de esta conexión: no revertir ni tocar el esquema compartido.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TEMP TABLE "Users" (
                "Id" integer PRIMARY KEY, "Name" varchar(100) NOT NULL,
                "Email" varchar(254) NOT NULL UNIQUE CHECK ("Email" = lower(btrim("Email"))),
                "Role" text NOT NULL DEFAULT 'User', "CreatedAt" timestamptz NOT NULL
            ) ON COMMIT DROP;
            CREATE TEMP TABLE "Tickets" (
                "Id" integer PRIMARY KEY, "Title" varchar(100) NOT NULL, "Description" text NOT NULL,
                "CreatedAt" timestamptz NOT NULL, "Status" text NOT NULL,
                "Priority" text NOT NULL, "Category" text NOT NULL,
                "CreatedByUserId" integer REFERENCES "Users"("Id") ON DELETE RESTRICT,
                "AssignedToUserId" integer REFERENCES "Users"("Id") ON DELETE RESTRICT
            ) ON COMMIT DROP;
            INSERT INTO "Users" VALUES
                (1, 'Cuenta anterior', 'legacy@example.invalid', 'User', CURRENT_TIMESTAMP),
                (2, 'Técnico anterior', 'technician@example.invalid', 'Technician', CURRENT_TIMESTAMP);
            INSERT INTO "Tickets" VALUES
                (1, 'Ticket anterior', 'Conservar relaciones', CURRENT_TIMESTAMP, 'Open', 'Medium', 'Other', 1, 2),
                (2, 'Sin relaciones', 'Conservar campos', CURRENT_TIMESTAMP, 'Closed', 'High', 'Access', NULL, NULL);
            """);
        var beforeUsers = await db.Database.SqlQueryRaw<string>("""
            SELECT jsonb_agg(to_jsonb(u) ORDER BY "Id")::text AS "Value" FROM pg_temp."Users" u
            """).SingleAsync();
        var beforeTickets = await db.Database.SqlQueryRaw<string>("""
            SELECT jsonb_agg(to_jsonb(t) ORDER BY "Id")::text AS "Value" FROM pg_temp."Tickets" t
            """).SingleAsync();
        var generator = db.GetService<IMigrationsSqlGenerator>();
        foreach (var command in generator.Generate(new AddUserPasswordHash().UpOperations))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
        var afterUsers = await db.Database.SqlQueryRaw<string>("""
            SELECT jsonb_agg(to_jsonb(u) - 'PasswordHash' ORDER BY "Id")::text AS "Value" FROM pg_temp."Users" u
            """).SingleAsync();
        var afterTickets = await db.Database.SqlQueryRaw<string>("""
            SELECT jsonb_agg(to_jsonb(t) ORDER BY "Id")::text AS "Value" FROM pg_temp."Tickets" t
            """).SingleAsync();
        Assert.Equal(beforeUsers, afterUsers);
        Assert.Equal(beforeTickets, afterTickets);
        Assert.True(await db.Database.SqlQueryRaw<bool>("""
            SELECT bool_and("PasswordHash" IS NULL) AS "Value" FROM pg_temp."Users"
            """).SingleAsync());
        Assert.Equal(2, await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::integer AS "Value" FROM pg_constraint
            WHERE conrelid = 'pg_temp."Tickets"'::regclass AND contype = 'f' AND confdeltype = 'r'
            """).SingleAsync());
        await transaction.CommitAsync(); // Elimina solo las dos tablas temporales (ON COMMIT DROP).
    }
}
