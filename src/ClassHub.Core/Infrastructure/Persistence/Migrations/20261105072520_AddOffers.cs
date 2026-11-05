using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Offers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    PriceMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    Mode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    LessonCount = table.Column<int>(type: "int", nullable: true),
                    Seats = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ShowOnPublicSite = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Offers", x => x.Id);
                    table.UniqueConstraint("AK_Offers_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_Offers_Currency", "[Currency] LIKE '[A-Z][A-Z][A-Z]'");
                    table.CheckConstraint("CK_Offers_Duration", "[DurationMinutes] BETWEEN 15 AND 600");
                    table.CheckConstraint("CK_Offers_LessonCount", "([Kind] = 'Package' AND [LessonCount] BETWEEN 1 AND 200) OR ([Kind] <> 'Package' AND [LessonCount] IS NULL)");
                    table.CheckConstraint("CK_Offers_PriceMinor", "[PriceMinor] >= 0 AND [PriceMinor] <= 1000000000");
                    table.CheckConstraint("CK_Offers_Seats", "([Kind] = 'GroupProgram' AND [Seats] BETWEEN 1 AND 500) OR ([Kind] <> 'GroupProgram' AND [Seats] IS NULL)");
                    table.ForeignKey(
                        name: "FK_Offers_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Offers_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Offers_SubjectId",
                table: "Offers",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Offers_WorkspaceId",
                table: "Offers",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Offers_WorkspaceId_DisplayOrder",
                table: "Offers",
                columns: new[] { "WorkspaceId", "DisplayOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Offers");
        }
    }
}
