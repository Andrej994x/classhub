using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHomework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Homework",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DueAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Homework", x => x.Id);
                    table.UniqueConstraint("AK_Homework_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_Homework_Due", "([DueDate] IS NULL AND [DueAtUtc] IS NULL) OR ([DueDate] IS NOT NULL AND [DueAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_Homework_Title", "LEN([Title]) >= 1");
                    table.ForeignKey(
                        name: "FK_Homework_Lessons_WorkspaceId_LessonId",
                        columns: x => new { x.WorkspaceId, x.LessonId },
                        principalTable: "Lessons",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                    table.ForeignKey(
                        name: "FK_Homework_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "HomeworkAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HomeworkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    AnswerText = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ContentType = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    TeacherComment = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HomeworkAssignments", x => x.Id);
                    table.CheckConstraint("CK_HomeworkAssignments_File", "([FileId] IS NULL AND [FileName] IS NULL AND [ContentType] IS NULL AND [SizeBytes] IS NULL) OR ([FileId] IS NOT NULL AND [FileName] IS NOT NULL AND [ContentType] IS NOT NULL AND [SizeBytes] > 0)");
                    table.CheckConstraint("CK_HomeworkAssignments_Reviewed", "([Status] = 3 AND [ReviewedAtUtc] IS NOT NULL) OR ([Status] <> 3 AND [ReviewedAtUtc] IS NULL)");
                    table.CheckConstraint("CK_HomeworkAssignments_Status", "[Status] BETWEEN 1 AND 3");
                    table.CheckConstraint("CK_HomeworkAssignments_Submitted", "[Status] = 1 OR [SubmittedAtUtc] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_HomeworkAssignments_Homework_WorkspaceId_HomeworkId",
                        columns: x => new { x.WorkspaceId, x.HomeworkId },
                        principalTable: "Homework",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                    table.ForeignKey(
                        name: "FK_HomeworkAssignments_StoredFiles_WorkspaceId_FileId",
                        columns: x => new { x.WorkspaceId, x.FileId },
                        principalTable: "StoredFiles",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                    table.ForeignKey(
                        name: "FK_HomeworkAssignments_Students_WorkspaceId_StudentId",
                        columns: x => new { x.WorkspaceId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Homework_WorkspaceId",
                table: "Homework",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Homework_WorkspaceId_ArchivedAtUtc_UpdatedAtUtc",
                table: "Homework",
                columns: new[] { "WorkspaceId", "ArchivedAtUtc", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Homework_WorkspaceId_LessonId",
                table: "Homework",
                columns: new[] { "WorkspaceId", "LessonId" });

            migrationBuilder.CreateIndex(
                name: "IX_HomeworkAssignments_FileId",
                table: "HomeworkAssignments",
                column: "FileId",
                unique: true,
                filter: "[FileId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HomeworkAssignments_HomeworkId_StudentId",
                table: "HomeworkAssignments",
                columns: new[] { "HomeworkId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HomeworkAssignments_WorkspaceId",
                table: "HomeworkAssignments",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_HomeworkAssignments_WorkspaceId_FileId",
                table: "HomeworkAssignments",
                columns: new[] { "WorkspaceId", "FileId" });

            migrationBuilder.CreateIndex(
                name: "IX_HomeworkAssignments_WorkspaceId_HomeworkId",
                table: "HomeworkAssignments",
                columns: new[] { "WorkspaceId", "HomeworkId" });

            migrationBuilder.CreateIndex(
                name: "IX_HomeworkAssignments_WorkspaceId_StudentId",
                table: "HomeworkAssignments",
                columns: new[] { "WorkspaceId", "StudentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HomeworkAssignments");

            migrationBuilder.DropTable(
                name: "Homework");
        }
    }
}
