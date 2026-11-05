using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlansAndSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    NameMk = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlanLimits",
                columns: table => new
                {
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    Value = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanLimits", x => new { x.PlanId, x.Key });
                    table.CheckConstraint("CK_PlanLimits_Value", "[Value] BETWEEN 0 AND 1000000");
                    table.ForeignKey(
                        name: "FK_PlanLimits_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodStartUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PeriodEndUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    Months = table.Column<int>(type: "int", nullable: true),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: true),
                    Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: true),
                    PaymentMethod = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    PaymentReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PaidOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionEvents", x => x.Id);
                    table.CheckConstraint("CK_SubscriptionEvents_Amount", "[AmountMinor] IS NULL OR [AmountMinor] BETWEEN 0 AND 100000000000");
                    table.CheckConstraint("CK_SubscriptionEvents_Currency", "[Currency] IS NULL OR [Currency] LIKE '[A-Z][A-Z][A-Z]'");
                    table.CheckConstraint("CK_SubscriptionEvents_Kind", "[Kind] IN (1, 2, 3)");
                    table.CheckConstraint("CK_SubscriptionEvents_Payment", "([Kind] IN (1, 2) AND [Months] IS NOT NULL AND [Months] BETWEEN 1 AND 36 AND [AmountMinor] IS NOT NULL AND [Currency] IS NOT NULL AND [PaymentMethod] IS NOT NULL AND [PaidOn] IS NOT NULL) OR ([Kind] = 3 AND [AmountMinor] IS NULL AND [Months] IS NULL)");
                    table.ForeignKey(
                        name: "FK_SubscriptionEvents_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SubscriptionEvents_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceSubscriptions", x => x.Id);
                    table.CheckConstraint("CK_WorkspaceSubscriptions_Period", "[EndsAtUtc] IS NULL OR [EndsAtUtc] > [StartsAtUtc]");
                    table.ForeignKey(
                        name: "FK_WorkspaceSubscriptions_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkspaceSubscriptions_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "Plans",
                columns: new[] { "Id", "Code", "IsDefault", "NameEn", "NameMk", "SortOrder", "UpdatedAtUtc", "Version" },
                values: new object[,]
                {
                    { new Guid("8a5c1e2f-3b4d-4e6f-9a0b-000000000001"), "free", true, "Free", "Бесплатен", 1, new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { new Guid("8a5c1e2f-3b4d-4e6f-9a0b-000000000002"), "pro", false, "Pro", "Про", 2, new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Utc), 0 }
                });

            migrationBuilder.InsertData(
                table: "PlanLimits",
                columns: new[] { "Key", "PlanId", "Value" },
                values: new object[,]
                {
                    { "active-students", new Guid("8a5c1e2f-3b4d-4e6f-9a0b-000000000001"), 10 },
                    { "activities", new Guid("8a5c1e2f-3b4d-4e6f-9a0b-000000000001"), 10 },
                    { "analytics-level", new Guid("8a5c1e2f-3b4d-4e6f-9a0b-000000000001"), 1 },
                    { "published-tests", new Guid("8a5c1e2f-3b4d-4e6f-9a0b-000000000001"), 3 },
                    { "storage-mb", new Guid("8a5c1e2f-3b4d-4e6f-9a0b-000000000001"), 500 },
                    { "active-students", new Guid("8a5c1e2f-3b4d-4e6f-9a0b-000000000002"), 150 },
                    { "activities", new Guid("8a5c1e2f-3b4d-4e6f-9a0b-000000000002"), 500 },
                    { "analytics-level", new Guid("8a5c1e2f-3b4d-4e6f-9a0b-000000000002"), 2 },
                    { "published-tests", new Guid("8a5c1e2f-3b4d-4e6f-9a0b-000000000002"), 50 },
                    { "storage-mb", new Guid("8a5c1e2f-3b4d-4e6f-9a0b-000000000002"), 10240 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Plans_Code",
                table: "Plans",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Plans_IsDefault",
                table: "Plans",
                column: "IsDefault",
                unique: true,
                filter: "[IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionEvents_OccurredAtUtc",
                table: "SubscriptionEvents",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionEvents_PlanId",
                table: "SubscriptionEvents",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionEvents_WorkspaceId",
                table: "SubscriptionEvents",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionEvents_WorkspaceId_OccurredAtUtc",
                table: "SubscriptionEvents",
                columns: new[] { "WorkspaceId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceSubscriptions_PlanId_EndsAtUtc",
                table: "WorkspaceSubscriptions",
                columns: new[] { "PlanId", "EndsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceSubscriptions_WorkspaceId",
                table: "WorkspaceSubscriptions",
                column: "WorkspaceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlanLimits");

            migrationBuilder.DropTable(
                name: "SubscriptionEvents");

            migrationBuilder.DropTable(
                name: "WorkspaceSubscriptions");

            migrationBuilder.DropTable(
                name: "Plans");
        }
    }
}
