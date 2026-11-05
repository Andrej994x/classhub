using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTestAttemptLeadForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TestAttempts_WorkspaceId_LeadId",
                table: "TestAttempts",
                columns: new[] { "WorkspaceId", "LeadId" });

            migrationBuilder.AddForeignKey(
                name: "FK_TestAttempts_Leads_WorkspaceId_LeadId",
                table: "TestAttempts",
                columns: new[] { "WorkspaceId", "LeadId" },
                principalTable: "Leads",
                principalColumns: new[] { "WorkspaceId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TestAttempts_Leads_WorkspaceId_LeadId",
                table: "TestAttempts");

            migrationBuilder.DropIndex(
                name: "IX_TestAttempts_WorkspaceId_LeadId",
                table: "TestAttempts");
        }
    }
}
