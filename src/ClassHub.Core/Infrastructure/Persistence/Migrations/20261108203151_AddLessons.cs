using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLessons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Lessons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TeacherUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Topic = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StartUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    EndUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    TimeZone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    MeetingUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lessons", x => x.Id);
                    table.UniqueConstraint("AK_Lessons_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_Lessons_Kind", "([Kind] = 1 AND [GroupId] IS NULL) OR ([Kind] = 2 AND [GroupId] IS NOT NULL)");
                    table.CheckConstraint("CK_Lessons_Mode", "[Mode] IN (1, 2)");
                    table.CheckConstraint("CK_Lessons_Status", "[Status] BETWEEN 1 AND 3");
                    table.CheckConstraint("CK_Lessons_Time", "[EndUtc] > [StartUtc]");
                    table.CheckConstraint("CK_Lessons_Title", "LEN([Title]) >= 1");
                    table.ForeignKey(
                        name: "FK_Lessons_AspNetUsers_TeacherUserId",
                        column: x => x.TeacherUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Lessons_Groups_WorkspaceId_GroupId",
                        columns: x => new { x.WorkspaceId, x.GroupId },
                        principalTable: "Groups",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                    table.ForeignKey(
                        name: "FK_Lessons_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Lessons_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LessonParticipants",
                columns: table => new
                {
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonParticipants", x => new { x.LessonId, x.StudentId });
                    table.ForeignKey(
                        name: "FK_LessonParticipants_Lessons_WorkspaceId_LessonId",
                        columns: x => new { x.WorkspaceId, x.LessonId },
                        principalTable: "Lessons",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                    table.ForeignKey(
                        name: "FK_LessonParticipants_Students_WorkspaceId_StudentId",
                        columns: x => new { x.WorkspaceId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_LessonParticipants_WorkspaceId",
                table: "LessonParticipants",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonParticipants_WorkspaceId_LessonId",
                table: "LessonParticipants",
                columns: new[] { "WorkspaceId", "LessonId" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonParticipants_WorkspaceId_StudentId",
                table: "LessonParticipants",
                columns: new[] { "WorkspaceId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_SubjectId",
                table: "Lessons",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_TeacherUserId_StartUtc_EndUtc",
                table: "Lessons",
                columns: new[] { "TeacherUserId", "StartUtc", "EndUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_WorkspaceId",
                table: "Lessons",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_WorkspaceId_GroupId",
                table: "Lessons",
                columns: new[] { "WorkspaceId", "GroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_WorkspaceId_StartUtc",
                table: "Lessons",
                columns: new[] { "WorkspaceId", "StartUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LessonParticipants");

            migrationBuilder.DropTable(
                name: "Lessons");
        }
    }
}
