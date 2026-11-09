using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceAndLessonEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PrivateNote",
                table: "Lessons",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SharedSummary",
                table: "Lessons",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Attendance",
                table: "LessonParticipants",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LessonEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonEvents_Lessons_WorkspaceId_LessonId",
                        columns: x => new { x.WorkspaceId, x.LessonId },
                        principalTable: "Lessons",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_LessonParticipants_Attendance",
                table: "LessonParticipants",
                sql: "[Attendance] IS NULL OR [Attendance] BETWEEN 1 AND 3");

            migrationBuilder.CreateIndex(
                name: "IX_LessonEvents_LessonId_Kind",
                table: "LessonEvents",
                columns: new[] { "LessonId", "Kind" },
                unique: true,
                filter: "[Kind] = N'LessonCompleted'");

            migrationBuilder.CreateIndex(
                name: "IX_LessonEvents_ProcessedAtUtc_OccurredAtUtc",
                table: "LessonEvents",
                columns: new[] { "ProcessedAtUtc", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonEvents_WorkspaceId",
                table: "LessonEvents",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonEvents_WorkspaceId_LessonId",
                table: "LessonEvents",
                columns: new[] { "WorkspaceId", "LessonId" });

            // Lessons held before attendance existed: Present (D-22 default).
            migrationBuilder.Sql(ClassHub.Core.Modules.Lessons.Domain.LessonBackfills.CompletedLessonAttendanceSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LessonEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LessonParticipants_Attendance",
                table: "LessonParticipants");

            migrationBuilder.DropColumn(
                name: "PrivateNote",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "SharedSummary",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "Attendance",
                table: "LessonParticipants");
        }
    }
}
