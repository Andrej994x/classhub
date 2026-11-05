using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSitePageViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SitePageViews",
                columns: table => new
                {
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Views = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SitePageViews", x => new { x.SiteId, x.Day });
                    table.CheckConstraint("CK_SitePageViews_Views", "[Views] >= 0");
                    table.ForeignKey(
                        name: "FK_SitePageViews_Sites_WorkspaceId_SiteId",
                        columns: x => new { x.WorkspaceId, x.SiteId },
                        principalTable: "Sites",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_SitePageViews_WorkspaceId",
                table: "SitePageViews",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_SitePageViews_WorkspaceId_Day",
                table: "SitePageViews",
                columns: new[] { "WorkspaceId", "Day" });

            migrationBuilder.CreateIndex(
                name: "IX_SitePageViews_WorkspaceId_SiteId",
                table: "SitePageViews",
                columns: new[] { "WorkspaceId", "SiteId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SitePageViews");
        }
    }
}
