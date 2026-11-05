using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModerationAndCatalogAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "SubjectCategories",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HiddenAtUtc",
                table: "Sites",
                type: "datetime2(3)",
                precision: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HiddenReason",
                table: "Sites",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HiddenSectionsJson",
                table: "Sites",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "EducationLevels",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HiddenAtUtc",
                table: "DiagnosticTests",
                type: "datetime2(3)",
                precision: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HiddenReason",
                table: "DiagnosticTests",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AbuseReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Target = table.Column<int>(type: "int", nullable: false),
                    SectionType = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    TestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<int>(type: "int", nullable: false),
                    Details = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReporterEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolutionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbuseReports", x => x.Id);
                    table.CheckConstraint("CK_AbuseReports_Reason", "[Reason] BETWEEN 1 AND 6");
                    table.CheckConstraint("CK_AbuseReports_Resolution", "([Status] = 1 AND [ResolvedAtUtc] IS NULL) OR ([Status] <> 1 AND [ResolvedAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_AbuseReports_Status", "[Status] BETWEEN 1 AND 3");
                    table.CheckConstraint("CK_AbuseReports_Target", "([Target] = 1 AND [SectionType] IS NULL AND [TestId] IS NULL) OR ([Target] = 2 AND [SectionType] IS NOT NULL AND [TestId] IS NULL) OR ([Target] = 3 AND [SectionType] IS NULL AND [TestId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_AbuseReports_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id");
                });

            migrationBuilder.UpdateData(
                table: "EducationLevels",
                keyColumn: "Id",
                keyValue: new Guid("6c4d75c8-e8ce-85b2-855d-76a5de06548a"),
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "EducationLevels",
                keyColumn: "Id",
                keyValue: new Guid("966c9507-81fe-84d8-83da-eaee33bf0590"),
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "EducationLevels",
                keyColumn: "Id",
                keyValue: new Guid("9b6309c4-477c-8e90-8a3a-0d54e1072b33"),
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "EducationLevels",
                keyColumn: "Id",
                keyValue: new Guid("ba74857e-c742-8be2-a7ac-2e76cbd34bd1"),
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "EducationLevels",
                keyColumn: "Id",
                keyValue: new Guid("cef3e47e-3c42-88ec-be62-23fab9fabae8"),
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "EducationLevels",
                keyColumn: "Id",
                keyValue: new Guid("f8a251b8-dc51-84a0-9928-176ee8f36666"),
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "SubjectCategories",
                keyColumn: "Id",
                keyValue: new Guid("0db075ef-30e5-8580-be5f-7b3ca817e879"),
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "SubjectCategories",
                keyColumn: "Id",
                keyValue: new Guid("1315844f-76fb-8559-b56d-229e113ea93f"),
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "SubjectCategories",
                keyColumn: "Id",
                keyValue: new Guid("17e6cd67-6a49-8e7f-bf93-696f0aec817c"),
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "SubjectCategories",
                keyColumn: "Id",
                keyValue: new Guid("1e9da771-d8b2-82d1-9472-14cdae52ce29"),
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "SubjectCategories",
                keyColumn: "Id",
                keyValue: new Guid("397ff651-6b09-8299-9d88-c774a8cf0469"),
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "SubjectCategories",
                keyColumn: "Id",
                keyValue: new Guid("ea8e5715-d388-84ac-a08b-7be3ec73ccc5"),
                column: "IsActive",
                value: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sites_Hidden",
                table: "Sites",
                sql: "([HiddenAtUtc] IS NULL AND [HiddenReason] IS NULL) OR ([HiddenAtUtc] IS NOT NULL AND [HiddenReason] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sites_HiddenSectionsJson",
                table: "Sites",
                sql: "[HiddenSectionsJson] IS NULL OR ISJSON([HiddenSectionsJson]) = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DiagnosticTests_Hidden",
                table: "DiagnosticTests",
                sql: "([HiddenAtUtc] IS NULL AND [HiddenReason] IS NULL) OR ([HiddenAtUtc] IS NOT NULL AND [HiddenReason] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_AbuseReports_Status_CreatedAtUtc",
                table: "AbuseReports",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AbuseReports_WorkspaceId_CreatedAtUtc",
                table: "AbuseReports",
                columns: new[] { "WorkspaceId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbuseReports");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Sites_Hidden",
                table: "Sites");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Sites_HiddenSectionsJson",
                table: "Sites");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DiagnosticTests_Hidden",
                table: "DiagnosticTests");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "SubjectCategories");

            migrationBuilder.DropColumn(
                name: "HiddenAtUtc",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "HiddenReason",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "HiddenSectionsJson",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "EducationLevels");

            migrationBuilder.DropColumn(
                name: "HiddenAtUtc",
                table: "DiagnosticTests");

            migrationBuilder.DropColumn(
                name: "HiddenReason",
                table: "DiagnosticTests");
        }
    }
}
