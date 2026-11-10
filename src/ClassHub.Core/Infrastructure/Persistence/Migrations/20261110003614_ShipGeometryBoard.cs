using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShipGeometryBoard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Tools",
                keyColumn: "Id",
                keyValue: new Guid("7f1bc077-85d3-8050-b87c-be3f80599a31"),
                columns: new[] { "IsActive", "IsFeatured" },
                values: new object[] { true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Tools",
                keyColumn: "Id",
                keyValue: new Guid("7f1bc077-85d3-8050-b87c-be3f80599a31"),
                columns: new[] { "IsActive", "IsFeatured" },
                values: new object[] { false, false });
        }
    }
}
