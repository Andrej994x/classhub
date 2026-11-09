using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddToolbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tools",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    NameMk = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DescriptionMk = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    DescriptionEn = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Url = table.Column<string>(type: "varchar(400)", unicode: false, maxLength: 400, nullable: true),
                    BuiltInKey = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    LicenseNoteMk = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    LicenseNoteEn = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsFeatured = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tools", x => x.Id);
                    table.CheckConstraint("CK_Tools_Category", "[Category] BETWEEN 1 AND 6");
                    table.CheckConstraint("CK_Tools_Kind", "([Kind] = 1 AND [Url] IS NOT NULL AND [BuiltInKey] IS NULL) OR ([Kind] = 2 AND [BuiltInKey] IS NOT NULL AND [Url] IS NULL)");
                    table.CheckConstraint("CK_Tools_Url", "[Url] IS NULL OR [Url] LIKE 'https://%'");
                });

            migrationBuilder.CreateTable(
                name: "ToolSubjects",
                columns: table => new
                {
                    ToolId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToolSubjects", x => new { x.ToolId, x.SubjectId });
                    table.ForeignKey(
                        name: "FK_ToolSubjects_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ToolSubjects_Tools_ToolId",
                        column: x => x.ToolId,
                        principalTable: "Tools",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "Tools",
                columns: new[] { "Id", "BuiltInKey", "Category", "Code", "CreatedAtUtc", "DescriptionEn", "DescriptionMk", "IsActive", "IsFeatured", "Kind", "LicenseNoteEn", "LicenseNoteMk", "NameEn", "NameMk", "SortOrder", "UpdatedAtUtc", "Url", "Version" },
                values: new object[,]
                {
                    { new Guid("32f1e49c-bfaf-8602-8389-935cbebfcf81"), "drawing-board", 5, "drawing-board", new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "Free drawing, text, shapes, arrows and diagrams; save and reopen.", "Слободно цртање, текст, форми, стрелки и дијаграми; зачувување и повторно отворање.", false, false, 2, "Built into ClassHub.", "Вградена во ClassHub.", "Drawing board", "Табла за цртање", 50, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), null, 1 },
                    { new Guid("41e11b64-3665-8b4e-8beb-324b54514dd9"), null, 2, "desmos", new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "Plot functions, inequalities and tables, with sliders for parameters.", "Цртање графици на функции, неравенки и табели, со лизгачи за параметри.", true, true, 1, "Free to use on desmos.com under the Desmos terms of service.", "Бесплатно на desmos.com според условите за користење на Desmos.", "Desmos graphing calculator", "Desmos калкулатор за графици", 20, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "https://www.desmos.com/calculator", 1 },
                    { new Guid("787c3baf-dd00-8a1d-9463-5a9f1d1e36dd"), null, 4, "molview", new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "Draw molecules and view them in 3D.", "Цртање и 3D приказ на молекули.", true, false, 1, "Free web application; opened on molview.org.", "Бесплатна веб-апликација; се отвора на molview.org.", "MolView", "MolView", 40, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "https://molview.org/", 1 },
                    { new Guid("7f1bc077-85d3-8050-b87c-be3f80599a31"), "geometry-board", 1, "geometry-board", new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "Points, lines, angles, triangles, circles, a coordinate system and graphs; save and reopen.", "Точки, прави, агли, триаголници, кругови, координатен систем и графици; зачувување и повторно отворање.", false, false, 2, "Built into ClassHub.", "Вградена во ClassHub.", "Geometry board", "Геометриска табла", 60, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), null, 1 },
                    { new Guid("8d1326f7-2ad6-8aa3-8852-74e14f7dd483"), null, 3, "phet", new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "Interactive simulations for physics, chemistry, mathematics and biology.", "Интерактивни симулации по физика, хемија, математика и биологија.", true, true, 1, "Free simulations (CC BY 4.0) by the University of Colorado Boulder; opened on phet.colorado.edu.", "Бесплатни симулации (CC BY 4.0) од Универзитетот во Колорадо Болдер; се отвораат на phet.colorado.edu.", "PhET interactive simulations", "PhET интерактивни симулации", 30, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "https://phet.colorado.edu/", 1 },
                    { new Guid("a55d46a7-f68a-8314-aaa2-1bf162dae108"), null, 1, "geogebra", new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "Geometry, algebra, graphs and spreadsheets in one; constructions with points, lines, circles and functions.", "Геометрија, алгебра, графици и табели во едно; конструкции со точки, прави, кругови и функции.", true, true, 1, "Free for non-commercial use under the GeoGebra license; opened on geogebra.org.", "Бесплатно за некомерцијална употреба според лиценцата на GeoGebra; се отвора на geogebra.org.", "GeoGebra", "GeoGebra", 10, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "https://www.geogebra.org/classic", 1 }
                });

            migrationBuilder.InsertData(
                table: "ToolSubjects",
                columns: new[] { "SubjectId", "ToolId" },
                values: new object[,]
                {
                    { new Guid("44136582-6911-86d2-93a3-6313a3781ba5"), new Guid("41e11b64-3665-8b4e-8beb-324b54514dd9") },
                    { new Guid("31006688-ac16-8b8b-bee9-870edeb892cf"), new Guid("787c3baf-dd00-8a1d-9463-5a9f1d1e36dd") },
                    { new Guid("e962a711-7192-8d10-be32-bdcb2ce2b979"), new Guid("787c3baf-dd00-8a1d-9463-5a9f1d1e36dd") },
                    { new Guid("44136582-6911-86d2-93a3-6313a3781ba5"), new Guid("7f1bc077-85d3-8050-b87c-be3f80599a31") },
                    { new Guid("31006688-ac16-8b8b-bee9-870edeb892cf"), new Guid("8d1326f7-2ad6-8aa3-8852-74e14f7dd483") },
                    { new Guid("3669c55e-f275-87a0-9cd8-a34178c16196"), new Guid("8d1326f7-2ad6-8aa3-8852-74e14f7dd483") },
                    { new Guid("44136582-6911-86d2-93a3-6313a3781ba5"), new Guid("8d1326f7-2ad6-8aa3-8852-74e14f7dd483") },
                    { new Guid("e962a711-7192-8d10-be32-bdcb2ce2b979"), new Guid("8d1326f7-2ad6-8aa3-8852-74e14f7dd483") },
                    { new Guid("3669c55e-f275-87a0-9cd8-a34178c16196"), new Guid("a55d46a7-f68a-8314-aaa2-1bf162dae108") },
                    { new Guid("44136582-6911-86d2-93a3-6313a3781ba5"), new Guid("a55d46a7-f68a-8314-aaa2-1bf162dae108") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tools_BuiltInKey",
                table: "Tools",
                column: "BuiltInKey",
                unique: true,
                filter: "[BuiltInKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Tools_Code",
                table: "Tools",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ToolSubjects_SubjectId",
                table: "ToolSubjects",
                column: "SubjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ToolSubjects");

            migrationBuilder.DropTable(
                name: "Tools");
        }
    }
}
