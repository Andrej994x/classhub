using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShipDrawingBoard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Tools",
                keyColumn: "Id",
                keyValue: new Guid("32f1e49c-bfaf-8602-8389-935cbebfcf81"),
                columns: new[] { "IsActive", "IsFeatured" },
                values: new object[] { true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Tools",
                keyColumn: "Id",
                keyValue: new Guid("32f1e49c-bfaf-8602-8389-935cbebfcf81"),
                columns: new[] { "IsActive", "IsFeatured" },
                values: new object[] { false, false });
        }
    }
}
