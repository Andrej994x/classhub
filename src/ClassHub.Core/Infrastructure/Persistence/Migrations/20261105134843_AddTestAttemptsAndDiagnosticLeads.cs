using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTestAttemptsAndDiagnosticLeads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Leads_Type",
                table: "Leads");

            migrationBuilder.AddColumn<Guid>(
                name: "AttemptId",
                table: "Leads",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TestAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnswersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Points = table.Column<int>(type: "int", nullable: false),
                    MaxPoints = table.Column<int>(type: "int", nullable: false),
                    CorrectQuestions = table.Column<int>(type: "int", nullable: false),
                    TotalQuestions = table.Column<int>(type: "int", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    LeadId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResultSharedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestAttempts", x => x.Id);
                    table.UniqueConstraint("AK_TestAttempts_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_TestAttempts_AnswersJson", "ISJSON([AnswersJson]) = 1");
                    table.CheckConstraint("CK_TestAttempts_Score", "[Points] >= 0 AND [Points] <= [MaxPoints] AND [CorrectQuestions] >= 0 AND [CorrectQuestions] <= [TotalQuestions]");
                    table.CheckConstraint("CK_TestAttempts_Shared", "[ResultSharedAtUtc] IS NULL OR [LeadId] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_TestAttempts_DiagnosticTests_WorkspaceId_TestId",
                        columns: x => new { x.WorkspaceId, x.TestId },
                        principalTable: "DiagnosticTests",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_AttemptId",
                table: "Leads",
                column: "AttemptId",
                unique: true,
                filter: "[AttemptId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_WorkspaceId_AttemptId",
                table: "Leads",
                columns: new[] { "WorkspaceId", "AttemptId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Leads_Attempt",
                table: "Leads",
                sql: "([Type] = 3 AND [AttemptId] IS NOT NULL) OR ([Type] <> 3 AND [AttemptId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Leads_Type",
                table: "Leads",
                sql: "[Type] IN (1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_TestAttempts_LeadId",
                table: "TestAttempts",
                column: "LeadId",
                unique: true,
                filter: "[LeadId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TestAttempts_WorkspaceId",
                table: "TestAttempts",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_TestAttempts_WorkspaceId_TestId_CompletedAtUtc",
                table: "TestAttempts",
                columns: new[] { "WorkspaceId", "TestId", "CompletedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_Leads_TestAttempts_WorkspaceId_AttemptId",
                table: "Leads",
                columns: new[] { "WorkspaceId", "AttemptId" },
                principalTable: "TestAttempts",
                principalColumns: new[] { "WorkspaceId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Leads_TestAttempts_WorkspaceId_AttemptId",
                table: "Leads");

            migrationBuilder.DropTable(
                name: "TestAttempts");

            migrationBuilder.DropIndex(
                name: "IX_Leads_AttemptId",
                table: "Leads");

            migrationBuilder.DropIndex(
                name: "IX_Leads_WorkspaceId_AttemptId",
                table: "Leads");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Leads_Attempt",
                table: "Leads");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Leads_Type",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "AttemptId",
                table: "Leads");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Leads_Type",
                table: "Leads",
                sql: "[Type] IN (1, 2)");
        }
    }
}
