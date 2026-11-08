using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookingRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OfferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OfferTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    EndUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    TimeZone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingRequests", x => x.Id);
                    table.CheckConstraint("CK_BookingRequests_Lesson", "([Status] IN (2, 4)) OR [LessonId] IS NULL");
                    table.CheckConstraint("CK_BookingRequests_Mode", "[Mode] IN (1, 2)");
                    table.CheckConstraint("CK_BookingRequests_Status", "[Status] BETWEEN 1 AND 4");
                    table.CheckConstraint("CK_BookingRequests_Time", "[EndUtc] > [StartUtc]");
                    table.ForeignKey(
                        name: "FK_BookingRequests_Leads_WorkspaceId_LeadId",
                        columns: x => new { x.WorkspaceId, x.LeadId },
                        principalTable: "Leads",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                    table.ForeignKey(
                        name: "FK_BookingRequests_Lessons_WorkspaceId_LessonId",
                        columns: x => new { x.WorkspaceId, x.LessonId },
                        principalTable: "Lessons",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                    table.ForeignKey(
                        name: "FK_BookingRequests_Offers_WorkspaceId_OfferId",
                        columns: x => new { x.WorkspaceId, x.OfferId },
                        principalTable: "Offers",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "BookingSettings",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    SlotStepMinutes = table.Column<int>(type: "int", nullable: false),
                    MinNoticeHours = table.Column<int>(type: "int", nullable: false),
                    HorizonDays = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingSettings", x => x.WorkspaceId);
                    table.CheckConstraint("CK_BookingSettings_Horizon", "[HorizonDays] BETWEEN 7 AND 90");
                    table.CheckConstraint("CK_BookingSettings_Notice", "[MinNoticeHours] BETWEEN 0 AND 168");
                    table.CheckConstraint("CK_BookingSettings_Step", "[SlotStepMinutes] IN (15, 30, 60)");
                    table.ForeignKey(
                        name: "FK_BookingSettings_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TimeOff",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeOff", x => x.Id);
                    table.CheckConstraint("CK_TimeOff_Dates", "[EndDate] >= [StartDate]");
                    table.ForeignKey(
                        name: "FK_TimeOff_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WorkingHours",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DayOfWeek = table.Column<int>(type: "int", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkingHours", x => x.Id);
                    table.CheckConstraint("CK_WorkingHours_Day", "[DayOfWeek] BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_WorkingHours_Time", "[EndTime] > [StartTime]");
                    table.ForeignKey(
                        name: "FK_WorkingHours_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingRequests_LeadId",
                table: "BookingRequests",
                column: "LeadId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingRequests_LessonId",
                table: "BookingRequests",
                column: "LessonId",
                unique: true,
                filter: "[LessonId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BookingRequests_WorkspaceId",
                table: "BookingRequests",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingRequests_WorkspaceId_LeadId",
                table: "BookingRequests",
                columns: new[] { "WorkspaceId", "LeadId" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingRequests_WorkspaceId_LessonId",
                table: "BookingRequests",
                columns: new[] { "WorkspaceId", "LessonId" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingRequests_WorkspaceId_OfferId",
                table: "BookingRequests",
                columns: new[] { "WorkspaceId", "OfferId" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingRequests_WorkspaceId_Status_StartUtc",
                table: "BookingRequests",
                columns: new[] { "WorkspaceId", "Status", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingSettings_WorkspaceId",
                table: "BookingSettings",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeOff_WorkspaceId",
                table: "TimeOff",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeOff_WorkspaceId_StartDate",
                table: "TimeOff",
                columns: new[] { "WorkspaceId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkingHours_WorkspaceId",
                table: "WorkingHours",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkingHours_WorkspaceId_DayOfWeek",
                table: "WorkingHours",
                columns: new[] { "WorkspaceId", "DayOfWeek" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingRequests");

            migrationBuilder.DropTable(
                name: "BookingSettings");

            migrationBuilder.DropTable(
                name: "TimeOff");

            migrationBuilder.DropTable(
                name: "WorkingHours");
        }
    }
}
