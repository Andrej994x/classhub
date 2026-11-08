using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "Students",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StudentInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    TokenHash = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    AcceptedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GuardianConsentAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentInvitations", x => x.Id);
                    table.CheckConstraint("CK_StudentInvitations_Email", "[Email] = LOWER([Email]) AND [Email] LIKE '%_@_%'");
                    table.ForeignKey(
                        name: "FK_StudentInvitations_AspNetUsers_AcceptedByUserId",
                        column: x => x.AcceptedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StudentInvitations_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StudentInvitations_Students_WorkspaceId_StudentId",
                        columns: x => new { x.WorkspaceId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Students_UserId",
                table: "Students",
                column: "UserId",
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Students_WorkspaceId_UserId",
                table: "Students",
                columns: new[] { "WorkspaceId", "UserId" },
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StudentInvitations_AcceptedByUserId",
                table: "StudentInvitations",
                column: "AcceptedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentInvitations_CreatedByUserId",
                table: "StudentInvitations",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentInvitations_TokenHash",
                table: "StudentInvitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentInvitations_WorkspaceId",
                table: "StudentInvitations",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentInvitations_WorkspaceId_StudentId_CreatedAtUtc",
                table: "StudentInvitations",
                columns: new[] { "WorkspaceId", "StudentId", "CreatedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_Students_AspNetUsers_UserId",
                table: "Students",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Students_AspNetUsers_UserId",
                table: "Students");

            migrationBuilder.DropTable(
                name: "StudentInvitations");

            migrationBuilder.DropIndex(
                name: "IX_Students_UserId",
                table: "Students");

            migrationBuilder.DropIndex(
                name: "IX_Students_WorkspaceId_UserId",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Students");
        }
    }
}
