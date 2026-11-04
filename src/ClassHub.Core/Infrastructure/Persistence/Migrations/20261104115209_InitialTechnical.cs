using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialTechnical : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SchemaInfo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchemaInfo", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "SchemaInfo",
                columns: new[] { "Id", "Key", "UpdatedAtUtc", "Value" },
                values: new object[] { 1, "schema.foundation", new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), "R0-P2" });

            migrationBuilder.CreateIndex(
                name: "IX_SchemaInfo_Key",
                table: "SchemaInfo",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SchemaInfo");
        }
    }
}
