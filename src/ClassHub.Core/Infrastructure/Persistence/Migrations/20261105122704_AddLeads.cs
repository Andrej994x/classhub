using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Leads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    OfferId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OfferTitle = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConsentVersion = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    ConsentedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leads", x => x.Id);
                    table.UniqueConstraint("AK_Leads_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_Leads_Email", "[Email] LIKE '%_@_%'");
                    table.CheckConstraint("CK_Leads_Name", "LEN([Name]) > 0");
                    table.CheckConstraint("CK_Leads_Status", "[Status] BETWEEN 1 AND 4");
                    table.CheckConstraint("CK_Leads_Type", "[Type] IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_Leads_Offers_WorkspaceId_OfferId",
                        columns: x => new { x.WorkspaceId, x.OfferId },
                        principalTable: "Offers",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                    table.ForeignKey(
                        name: "FK_Leads_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Leads_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LeadNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadNotes", x => x.Id);
                    table.CheckConstraint("CK_LeadNotes_Text", "LEN([Text]) > 0");
                    table.ForeignKey(
                        name: "FK_LeadNotes_Leads_WorkspaceId_LeadId",
                        columns: x => new { x.WorkspaceId, x.LeadId },
                        principalTable: "Leads",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeadNotes_WorkspaceId",
                table: "LeadNotes",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_LeadNotes_WorkspaceId_LeadId_CreatedAtUtc",
                table: "LeadNotes",
                columns: new[] { "WorkspaceId", "LeadId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_SubjectId",
                table: "Leads",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_WorkspaceId",
                table: "Leads",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_WorkspaceId_CreatedAtUtc",
                table: "Leads",
                columns: new[] { "WorkspaceId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_WorkspaceId_OfferId",
                table: "Leads",
                columns: new[] { "WorkspaceId", "OfferId" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_WorkspaceId_Status",
                table: "Leads",
                columns: new[] { "WorkspaceId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeadNotes");

            migrationBuilder.DropTable(
                name: "Leads");
        }
    }
}
