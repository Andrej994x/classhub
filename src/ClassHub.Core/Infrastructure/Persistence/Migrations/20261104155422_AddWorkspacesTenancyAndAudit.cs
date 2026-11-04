using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspacesTenancyAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TargetId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DetailsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequestId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                    table.CheckConstraint("CK_AuditLog_DetailsJson", "[DetailsJson] IS NULL OR ISJSON([DetailsJson]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "Workspaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TimeZone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    Locale = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workspaces", x => x.Id);
                    table.CheckConstraint("CK_Workspaces_Currency", "[Currency] LIKE '[A-Z][A-Z][A-Z]'");
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspaceMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkspaceMembers_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { new Guid("6f1c2d3e-0a4b-4c5d-8e9f-000000000001"), "5b0d1f6e-platform-owner", "PlatformOwner", "PLATFORMOWNER" },
                    { new Guid("6f1c2d3e-0a4b-4c5d-8e9f-000000000002"), "5b0d1f6e-platform-admin", "PlatformAdmin", "PLATFORMADMIN" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_ActorUserId_OccurredAtUtc",
                table: "AuditLog",
                columns: new[] { "ActorUserId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_OccurredAtUtc",
                table: "AuditLog",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_WorkspaceId_OccurredAtUtc",
                table: "AuditLog",
                columns: new[] { "WorkspaceId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceMembers_UserId",
                table: "WorkspaceMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceMembers_WorkspaceId",
                table: "WorkspaceMembers",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceMembers_WorkspaceId_UserId",
                table: "WorkspaceMembers",
                columns: new[] { "WorkspaceId", "UserId" },
                unique: true,
                filter: "[RevokedAtUtc] IS NULL");

            // Accounts registered before workspaces existed (R0-P4) get the workspace that registration now
            // provisions: same defaults (name in the account language, account time zone, MKD), an Owner
            // membership and an audit entry. Idempotent: only accounts without any membership.
            migrationBuilder.Sql(BackfillWorkspacesSql);
        }

        internal const string BackfillWorkspacesSql = """
            DECLARE @now datetime2(3) = SYSUTCDATETIME();
            DECLARE @backfill TABLE (UserId uniqueidentifier PRIMARY KEY, WorkspaceId uniqueidentifier NOT NULL, Locale nvarchar(10) NOT NULL, TimeZone nvarchar(64) NOT NULL);

            INSERT INTO @backfill (UserId, WorkspaceId, Locale, TimeZone)
            SELECT u.[Id], NEWID(), CASE WHEN u.[Locale] = N'en' THEN N'en' ELSE N'mk' END, u.[TimeZone]
            FROM [AspNetUsers] AS u
            WHERE NOT EXISTS (SELECT 1 FROM [WorkspaceMembers] AS m WHERE m.[UserId] = u.[Id]);

            INSERT INTO [Workspaces] ([Id], [Name], [TimeZone], [Currency], [Locale], [CreatedAtUtc])
            SELECT b.WorkspaceId, CASE WHEN b.Locale = N'en' THEN N'My workspace' ELSE N'Мој работен простор' END, b.TimeZone, 'MKD', b.Locale, @now
            FROM @backfill AS b;

            INSERT INTO [WorkspaceMembers] ([Id], [WorkspaceId], [UserId], [Role], [CreatedAtUtc], [RevokedAtUtc])
            SELECT NEWID(), b.WorkspaceId, b.UserId, N'Owner', @now, NULL
            FROM @backfill AS b;

            INSERT INTO [AuditLog] ([Id], [OccurredAtUtc], [ActorUserId], [Action], [TargetType], [TargetId], [WorkspaceId], [DetailsJson], [RequestId])
            SELECT NEWID(), @now, NULL, N'workspace.created', N'Workspace', CONVERT(nvarchar(36), b.WorkspaceId), b.WorkspaceId, N'{"via":"migration"}', NULL
            FROM @backfill AS b;
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLog");

            migrationBuilder.DropTable(
                name: "WorkspaceMembers");

            migrationBuilder.DropTable(
                name: "Workspaces");

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: new Guid("6f1c2d3e-0a4b-4c5d-8e9f-000000000001"));

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: new Guid("6f1c2d3e-0a4b-4c5d-8e9f-000000000002"));
        }
    }
}
