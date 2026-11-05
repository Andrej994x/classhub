using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiagnosticTests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DiagnosticTests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Slug = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Difficulty = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiagnosticTests", x => x.Id);
                    table.UniqueConstraint("AK_DiagnosticTests_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_DiagnosticTests_AttemptCount", "[AttemptCount] >= 0");
                    table.CheckConstraint("CK_DiagnosticTests_Difficulty", "[Difficulty] BETWEEN 1 AND 3");
                    table.CheckConstraint("CK_DiagnosticTests_Published", "[Status] = 1 OR [PublishedAtUtc] IS NOT NULL");
                    table.CheckConstraint("CK_DiagnosticTests_Slug", "LEN([Slug]) BETWEEN 3 AND 60 AND [Slug] NOT LIKE '%[^a-z0-9-]%'");
                    table.CheckConstraint("CK_DiagnosticTests_Status", "[Status] BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "FK_DiagnosticTests_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DiagnosticTests_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TestQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Points = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestQuestions", x => x.Id);
                    table.UniqueConstraint("AK_TestQuestions_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_TestQuestions_Points", "[Points] BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_TestQuestions_Text", "LEN([Text]) > 0");
                    table.ForeignKey(
                        name: "FK_TestQuestions_DiagnosticTests_WorkspaceId_TestId",
                        columns: x => new { x.WorkspaceId, x.TestId },
                        principalTable: "DiagnosticTests",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "TestOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestOptions", x => x.Id);
                    table.CheckConstraint("CK_TestOptions_Text", "LEN([Text]) > 0");
                    table.ForeignKey(
                        name: "FK_TestOptions_TestQuestions_WorkspaceId_QuestionId",
                        columns: x => new { x.WorkspaceId, x.QuestionId },
                        principalTable: "TestQuestions",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticTests_SubjectId",
                table: "DiagnosticTests",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticTests_WorkspaceId",
                table: "DiagnosticTests",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticTests_WorkspaceId_Slug",
                table: "DiagnosticTests",
                columns: new[] { "WorkspaceId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestOptions_QuestionId_Position",
                table: "TestOptions",
                columns: new[] { "QuestionId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_TestOptions_WorkspaceId",
                table: "TestOptions",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_TestOptions_WorkspaceId_QuestionId",
                table: "TestOptions",
                columns: new[] { "WorkspaceId", "QuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_TestQuestions_TestId_Position",
                table: "TestQuestions",
                columns: new[] { "TestId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_TestQuestions_WorkspaceId",
                table: "TestQuestions",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_TestQuestions_WorkspaceId_TestId",
                table: "TestQuestions",
                columns: new[] { "WorkspaceId", "TestId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TestOptions");

            migrationBuilder.DropTable(
                name: "TestQuestions");

            migrationBuilder.DropTable(
                name: "DiagnosticTests");
        }
    }
}
