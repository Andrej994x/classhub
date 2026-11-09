using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonReminders = table.Column<bool>(type: "bit", nullable: false),
                    HomeworkEmails = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_NotificationPreferences_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    DedupeKey = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TeacherUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LessonStartUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    LessonTitle = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    LessonTimeZone = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    HomeworkId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DueAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    LockedUntilUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.CheckConstraint("CK_Notifications_Kind", "[Kind] BETWEEN 1 AND 6");
                    table.CheckConstraint("CK_Notifications_Sent", "([Status] = 2 AND [SentAtUtc] IS NOT NULL) OR ([Status] <> 2 AND [SentAtUtc] IS NULL)");
                    table.CheckConstraint("CK_Notifications_Status", "[Status] BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_Notifications_Subject", "([Kind] = 5 AND [HomeworkId] IS NOT NULL AND [LessonId] IS NULL) OR ([Kind] <> 5 AND [LessonId] IS NOT NULL AND [HomeworkId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_Notifications_Students_WorkspaceId_StudentId",
                        columns: x => new { x.WorkspaceId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                    table.ForeignKey(
                        name: "FK_Notifications_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_DedupeKey",
                table: "Notifications",
                column: "DedupeKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Status_DueAtUtc",
                table: "Notifications",
                columns: new[] { "Status", "DueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_WorkspaceId",
                table: "Notifications",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_WorkspaceId_LessonId_StudentId",
                table: "Notifications",
                columns: new[] { "WorkspaceId", "LessonId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_WorkspaceId_StudentId",
                table: "Notifications",
                columns: new[] { "WorkspaceId", "StudentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationPreferences");

            migrationBuilder.DropTable(
                name: "Notifications");
        }
    }
}
