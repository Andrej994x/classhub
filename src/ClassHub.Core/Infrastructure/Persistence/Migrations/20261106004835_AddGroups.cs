using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Groups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Groups", x => x.Id);
                    table.UniqueConstraint("AK_Groups_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_Groups_Name", "LEN([Name]) >= 1");
                    table.ForeignKey(
                        name: "FK_Groups_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Groups_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "GroupMembers",
                columns: table => new
                {
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RemovedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupMembers", x => new { x.GroupId, x.StudentId });
                    table.CheckConstraint("CK_GroupMembers_Removed", "[RemovedAtUtc] IS NULL OR [RemovedAtUtc] >= [AddedAtUtc]");
                    table.ForeignKey(
                        name: "FK_GroupMembers_Groups_WorkspaceId_GroupId",
                        columns: x => new { x.WorkspaceId, x.GroupId },
                        principalTable: "Groups",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                    table.ForeignKey(
                        name: "FK_GroupMembers_Students_WorkspaceId_StudentId",
                        columns: x => new { x.WorkspaceId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembers_WorkspaceId",
                table: "GroupMembers",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembers_WorkspaceId_GroupId",
                table: "GroupMembers",
                columns: new[] { "WorkspaceId", "GroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembers_WorkspaceId_StudentId_RemovedAtUtc",
                table: "GroupMembers",
                columns: new[] { "WorkspaceId", "StudentId", "RemovedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Groups_SubjectId",
                table: "Groups",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Groups_WorkspaceId",
                table: "Groups",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Groups_WorkspaceId_ArchivedAtUtc_Name",
                table: "Groups",
                columns: new[] { "WorkspaceId", "ArchivedAtUtc", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupMembers");

            migrationBuilder.DropTable(
                name: "Groups");
        }
    }
}
