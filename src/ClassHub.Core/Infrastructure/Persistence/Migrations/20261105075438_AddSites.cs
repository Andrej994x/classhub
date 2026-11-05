using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slug = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Template = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Palette = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DraftJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PublishedJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sites", x => x.Id);
                    table.UniqueConstraint("AK_Sites_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_Sites_DraftJson", "ISJSON([DraftJson]) = 1");
                    table.CheckConstraint("CK_Sites_PublishedJson", "[PublishedJson] IS NULL OR ISJSON([PublishedJson]) = 1");
                    table.CheckConstraint("CK_Sites_Slug", "LEN([Slug]) BETWEEN 3 AND 40 AND [Slug] NOT LIKE '%[^a-z0-9-]%'");
                    table.ForeignKey(
                        name: "FK_Sites_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sites_Slug",
                table: "Sites",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sites_WorkspaceId",
                table: "Sites",
                column: "WorkspaceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sites");
        }
    }
}
