using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUsageCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UsageCounters",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Event = table.Column<byte>(type: "tinyint", nullable: false),
                    Subject = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageCounters", x => new { x.WorkspaceId, x.Day, x.Event, x.Subject });
                    table.CheckConstraint("CK_UsageCounters_Count", "[Count] >= 0");
                    table.CheckConstraint("CK_UsageCounters_Event", "[Event] BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "FK_UsageCounters_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsageCounters_Day_Event",
                table: "UsageCounters",
                columns: new[] { "Day", "Event" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageCounters_WorkspaceId",
                table: "UsageCounters",
                column: "WorkspaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UsageCounters");
        }
    }
}
