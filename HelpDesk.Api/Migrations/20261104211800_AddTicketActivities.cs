using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HelpDesk.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketActivities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TicketActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TicketId = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<int>(type: "integer", nullable: false),
                    ActorNameSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Field = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OldValue = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    NewValue = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    OldAssigneeNameSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    NewAssigneeNameSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketActivities", x => x.Id);
                    table.CheckConstraint("CK_TicketActivities_AssigneeNames", "(\"Field\" = 'AssignedToUserId' AND\n    (\"OldValue\" IS NULL) = (\"OldAssigneeNameSnapshot\" IS NULL) AND\n    (\"NewValue\" IS NULL) = (\"NewAssigneeNameSnapshot\" IS NULL)) OR\n(\"Field\" <> 'AssignedToUserId' AND \"OldAssigneeNameSnapshot\" IS NULL AND \"NewAssigneeNameSnapshot\" IS NULL)");
                    table.CheckConstraint("CK_TicketActivities_Changed", "\"OldValue\" IS DISTINCT FROM \"NewValue\"");
                    table.CheckConstraint("CK_TicketActivities_Field", "\"Field\" IN ('Status', 'Priority', 'Category', 'AssignedToUserId')");
                    table.CheckConstraint("CK_TicketActivities_Values", "(\"Field\" = 'Status' AND \"OldValue\" IS NOT NULL AND \"NewValue\" IS NOT NULL\n    AND \"OldValue\" IN ('Open','InProgress','Resolved','Closed') AND \"NewValue\" IN ('Open','InProgress','Resolved','Closed')) OR\n(\"Field\" = 'Priority' AND \"OldValue\" IS NOT NULL AND \"NewValue\" IS NOT NULL\n    AND \"OldValue\" IN ('Low','Medium','High','Critical') AND \"NewValue\" IN ('Low','Medium','High','Critical')) OR\n(\"Field\" = 'Category' AND \"OldValue\" IS NOT NULL AND \"NewValue\" IS NOT NULL\n    AND \"OldValue\" IN ('Hardware','Software','Network','Access','Other') AND \"NewValue\" IN ('Hardware','Software','Network','Access','Other')) OR\n(\"Field\" = 'AssignedToUserId' AND (\"OldValue\" IS NULL OR \"OldValue\" ~ '^[1-9][0-9]{0,9}$')\n    AND (\"NewValue\" IS NULL OR \"NewValue\" ~ '^[1-9][0-9]{0,9}$'))");
                    table.ForeignKey(
                        name: "FK_TicketActivities_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketActivities_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TicketActivities_ActorUserId",
                table: "TicketActivities",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketActivities_TicketId_Id",
                table: "TicketActivities",
                columns: new[] { "TicketId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TicketActivities");
        }
    }
}
