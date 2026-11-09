using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Materials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ContentType = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Materials", x => x.Id);
                    table.UniqueConstraint("AK_Materials_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_Materials_Size", "[SizeBytes] > 0");
                    table.CheckConstraint("CK_Materials_Title", "LEN([Title]) >= 1");
                    table.ForeignKey(
                        name: "FK_Materials_StoredFiles_WorkspaceId_FileId",
                        columns: x => new { x.WorkspaceId, x.FileId },
                        principalTable: "StoredFiles",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                    table.ForeignKey(
                        name: "FK_Materials_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Materials_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MaterialAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialAssignments", x => x.Id);
                    table.CheckConstraint("CK_MaterialAssignments_Target", "([StudentId] IS NULL AND [GroupId] IS NOT NULL) OR ([StudentId] IS NOT NULL AND [GroupId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_MaterialAssignments_Groups_WorkspaceId_GroupId",
                        columns: x => new { x.WorkspaceId, x.GroupId },
                        principalTable: "Groups",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                    table.ForeignKey(
                        name: "FK_MaterialAssignments_Materials_WorkspaceId_MaterialId",
                        columns: x => new { x.WorkspaceId, x.MaterialId },
                        principalTable: "Materials",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                    table.ForeignKey(
                        name: "FK_MaterialAssignments_Students_WorkspaceId_StudentId",
                        columns: x => new { x.WorkspaceId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialAssignments_MaterialId_GroupId",
                table: "MaterialAssignments",
                columns: new[] { "MaterialId", "GroupId" },
                unique: true,
                filter: "[GroupId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialAssignments_MaterialId_StudentId",
                table: "MaterialAssignments",
                columns: new[] { "MaterialId", "StudentId" },
                unique: true,
                filter: "[StudentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialAssignments_WorkspaceId",
                table: "MaterialAssignments",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialAssignments_WorkspaceId_GroupId",
                table: "MaterialAssignments",
                columns: new[] { "WorkspaceId", "GroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialAssignments_WorkspaceId_MaterialId",
                table: "MaterialAssignments",
                columns: new[] { "WorkspaceId", "MaterialId" });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialAssignments_WorkspaceId_StudentId",
                table: "MaterialAssignments",
                columns: new[] { "WorkspaceId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Materials_FileId",
                table: "Materials",
                column: "FileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Materials_SubjectId",
                table: "Materials",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Materials_WorkspaceId",
                table: "Materials",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Materials_WorkspaceId_ArchivedAtUtc_UpdatedAtUtc",
                table: "Materials",
                columns: new[] { "WorkspaceId", "ArchivedAtUtc", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Materials_WorkspaceId_FileId",
                table: "Materials",
                columns: new[] { "WorkspaceId", "FileId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaterialAssignments");

            migrationBuilder.DropTable(
                name: "Materials");
        }
    }
}
