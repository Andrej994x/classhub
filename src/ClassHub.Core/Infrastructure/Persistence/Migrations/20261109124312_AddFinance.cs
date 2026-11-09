using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BalanceAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BalanceAdjustments", x => x.Id);
                    table.CheckConstraint("CK_BalanceAdjustments_Amount", "[AmountMinor] <> 0");
                    table.CheckConstraint("CK_BalanceAdjustments_Reason", "LEN([Reason]) >= 1");
                    table.ForeignKey(
                        name: "FK_BalanceAdjustments_Students_WorkspaceId_StudentId",
                        columns: x => new { x.WorkspaceId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "StudentPackages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OfferId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    LessonCount = table.Column<int>(type: "int", nullable: false),
                    PriceMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentPackages", x => x.Id);
                    table.UniqueConstraint("AK_StudentPackages_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_StudentPackages_Lessons", "[LessonCount] BETWEEN 1 AND 500");
                    table.CheckConstraint("CK_StudentPackages_Price", "[PriceMinor] BETWEEN 0 AND 100000000000");
                    table.CheckConstraint("CK_StudentPackages_Title", "LEN([Title]) >= 1");
                    table.ForeignKey(
                        name: "FK_StudentPackages_Offers_WorkspaceId_OfferId",
                        columns: x => new { x.WorkspaceId, x.OfferId },
                        principalTable: "Offers",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                    table.ForeignKey(
                        name: "FK_StudentPackages_Students_WorkspaceId_StudentId",
                        columns: x => new { x.WorkspaceId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                    table.ForeignKey(
                        name: "FK_StudentPackages_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LessonLedger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Occurrence = table.Column<int>(type: "int", nullable: true),
                    ReversesId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonLedger", x => x.Id);
                    table.UniqueConstraint("AK_LessonLedger_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_LessonLedger_Kind", "[Kind] BETWEEN 1 AND 3");
                    table.CheckConstraint("CK_LessonLedger_Shape", "([Kind] = 1 AND [Quantity] = 1 AND [LessonId] IS NOT NULL AND [Occurrence] IS NOT NULL AND [ReversesId] IS NULL) OR ([Kind] = 2 AND [Quantity] <> 0 AND [PackageId] IS NOT NULL AND [Reason] IS NOT NULL AND [Occurrence] IS NULL AND [ReversesId] IS NULL) OR ([Kind] = 3 AND [Quantity] <> 0 AND [ReversesId] IS NOT NULL AND [Occurrence] IS NULL)");
                    table.ForeignKey(
                        name: "FK_LessonLedger_LessonLedger_WorkspaceId_ReversesId",
                        columns: x => new { x.WorkspaceId, x.ReversesId },
                        principalTable: "LessonLedger",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                    table.ForeignKey(
                        name: "FK_LessonLedger_Lessons_WorkspaceId_LessonId",
                        columns: x => new { x.WorkspaceId, x.LessonId },
                        principalTable: "Lessons",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                    table.ForeignKey(
                        name: "FK_LessonLedger_StudentPackages_WorkspaceId_PackageId",
                        columns: x => new { x.WorkspaceId, x.PackageId },
                        principalTable: "StudentPackages",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                    table.ForeignKey(
                        name: "FK_LessonLedger_Students_WorkspaceId_StudentId",
                        columns: x => new { x.WorkspaceId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    PaidOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReversesPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.UniqueConstraint("AK_Payments_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_Payments_Amount", "([AmountMinor] > 0 AND [ReversesPaymentId] IS NULL) OR ([AmountMinor] < 0 AND [ReversesPaymentId] IS NOT NULL)");
                    table.CheckConstraint("CK_Payments_Method", "[Method] BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "FK_Payments_Payments_WorkspaceId_ReversesPaymentId",
                        columns: x => new { x.WorkspaceId, x.ReversesPaymentId },
                        principalTable: "Payments",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                    table.ForeignKey(
                        name: "FK_Payments_StudentPackages_WorkspaceId_PackageId",
                        columns: x => new { x.WorkspaceId, x.PackageId },
                        principalTable: "StudentPackages",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                    table.ForeignKey(
                        name: "FK_Payments_Students_WorkspaceId_StudentId",
                        columns: x => new { x.WorkspaceId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_BalanceAdjustments_WorkspaceId",
                table: "BalanceAdjustments",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_BalanceAdjustments_WorkspaceId_StudentId_CreatedAtUtc",
                table: "BalanceAdjustments",
                columns: new[] { "WorkspaceId", "StudentId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonLedger_LessonId_StudentId_Occurrence",
                table: "LessonLedger",
                columns: new[] { "LessonId", "StudentId", "Occurrence" },
                unique: true,
                filter: "[Kind] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_LessonLedger_ReversesId",
                table: "LessonLedger",
                column: "ReversesId",
                unique: true,
                filter: "[ReversesId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LessonLedger_WorkspaceId",
                table: "LessonLedger",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonLedger_WorkspaceId_LessonId",
                table: "LessonLedger",
                columns: new[] { "WorkspaceId", "LessonId" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonLedger_WorkspaceId_PackageId",
                table: "LessonLedger",
                columns: new[] { "WorkspaceId", "PackageId" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonLedger_WorkspaceId_ReversesId",
                table: "LessonLedger",
                columns: new[] { "WorkspaceId", "ReversesId" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonLedger_WorkspaceId_StudentId_CreatedAtUtc",
                table: "LessonLedger",
                columns: new[] { "WorkspaceId", "StudentId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ReversesPaymentId",
                table: "Payments",
                column: "ReversesPaymentId",
                unique: true,
                filter: "[ReversesPaymentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_WorkspaceId",
                table: "Payments",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_WorkspaceId_PackageId",
                table: "Payments",
                columns: new[] { "WorkspaceId", "PackageId" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_WorkspaceId_PaidOn",
                table: "Payments",
                columns: new[] { "WorkspaceId", "PaidOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_WorkspaceId_ReversesPaymentId",
                table: "Payments",
                columns: new[] { "WorkspaceId", "ReversesPaymentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_WorkspaceId_StudentId_PaidOn",
                table: "Payments",
                columns: new[] { "WorkspaceId", "StudentId", "PaidOn" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentPackages_WorkspaceId",
                table: "StudentPackages",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentPackages_WorkspaceId_OfferId",
                table: "StudentPackages",
                columns: new[] { "WorkspaceId", "OfferId" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentPackages_WorkspaceId_StudentId_CreatedAtUtc",
                table: "StudentPackages",
                columns: new[] { "WorkspaceId", "StudentId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BalanceAdjustments");

            migrationBuilder.DropTable(
                name: "LessonLedger");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "StudentPackages");
        }
    }
}
