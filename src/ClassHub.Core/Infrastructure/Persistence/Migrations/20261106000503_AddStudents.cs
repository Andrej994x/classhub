using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStudents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StudentId",
                table: "Leads",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Students",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    IsMinor = table.Column<bool>(type: "bit", nullable: false),
                    GuardianName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    GuardianEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    GuardianPhone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    SourceLeadId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Students", x => x.Id);
                    table.UniqueConstraint("AK_Students_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_Students_Email", "[Email] IS NULL OR ([Email] = LOWER([Email]) AND [Email] LIKE '%_@_%')");
                    table.CheckConstraint("CK_Students_FirstName", "LEN([FirstName]) >= 1");
                    table.ForeignKey(
                        name: "FK_Students_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StudentNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentNotes", x => x.Id);
                    table.CheckConstraint("CK_StudentNotes_Text", "LEN([Text]) >= 1");
                    table.ForeignKey(
                        name: "FK_StudentNotes_Students_WorkspaceId_StudentId",
                        columns: x => new { x.WorkspaceId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "StudentSubjects",
                columns: table => new
                {
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentSubjects", x => new { x.StudentId, x.SubjectId });
                    table.ForeignKey(
                        name: "FK_StudentSubjects_Students_WorkspaceId_StudentId",
                        columns: x => new { x.WorkspaceId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                    table.ForeignKey(
                        name: "FK_StudentSubjects_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_WorkspaceId_StudentId",
                table: "Leads",
                columns: new[] { "WorkspaceId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentNotes_WorkspaceId",
                table: "StudentNotes",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentNotes_WorkspaceId_StudentId_CreatedAtUtc",
                table: "StudentNotes",
                columns: new[] { "WorkspaceId", "StudentId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Students_WorkspaceId",
                table: "Students",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Students_WorkspaceId_ArchivedAtUtc_LastName",
                table: "Students",
                columns: new[] { "WorkspaceId", "ArchivedAtUtc", "LastName" });

            migrationBuilder.CreateIndex(
                name: "IX_Students_WorkspaceId_Email",
                table: "Students",
                columns: new[] { "WorkspaceId", "Email" },
                unique: true,
                filter: "[Email] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StudentSubjects_SubjectId",
                table: "StudentSubjects",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentSubjects_WorkspaceId",
                table: "StudentSubjects",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentSubjects_WorkspaceId_StudentId",
                table: "StudentSubjects",
                columns: new[] { "WorkspaceId", "StudentId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Leads_Students_WorkspaceId_StudentId",
                table: "Leads",
                columns: new[] { "WorkspaceId", "StudentId" },
                principalTable: "Students",
                principalColumns: new[] { "WorkspaceId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Leads_Students_WorkspaceId_StudentId",
                table: "Leads");

            migrationBuilder.DropTable(
                name: "StudentNotes");

            migrationBuilder.DropTable(
                name: "StudentSubjects");

            migrationBuilder.DropTable(
                name: "Students");

            migrationBuilder.DropIndex(
                name: "IX_Leads_WorkspaceId_StudentId",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "StudentId",
                table: "Leads");
        }
    }
}
