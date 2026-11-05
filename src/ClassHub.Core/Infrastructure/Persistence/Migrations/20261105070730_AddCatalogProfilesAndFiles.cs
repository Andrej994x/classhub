using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogProfilesAndFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EducationLevels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    NameMk = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EducationLevels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StoredFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    ContentType = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    StorageKey = table.Column<string>(type: "varchar(300)", unicode: false, maxLength: 300, nullable: false),
                    Width = table.Column<int>(type: "int", nullable: true),
                    Height = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredFiles", x => x.Id);
                    table.UniqueConstraint("AK_StoredFiles_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "SubjectCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    NameMk = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubjectCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeacherProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Bio = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TeachingMode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CountryCode = table.Column<string>(type: "char(2)", unicode: false, fixedLength: true, maxLength: 2, nullable: true),
                    PhotoFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherProfiles", x => x.Id);
                    table.UniqueConstraint("AK_TeacherProfiles_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_TeacherProfiles_CountryCode", "[CountryCode] IS NULL OR [CountryCode] LIKE '[A-Z][A-Z]'");
                    table.ForeignKey(
                        name: "FK_TeacherProfiles_StoredFiles_WorkspaceId_PhotoFileId",
                        columns: x => new { x.WorkspaceId, x.PhotoFileId },
                        principalTable: "StoredFiles",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                    table.ForeignKey(
                        name: "FK_TeacherProfiles_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Subjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NameMk = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subjects_SubjectCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "SubjectCategories",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TeacherProfileLanguages",
                columns: table => new
                {
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LanguageCode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherProfileLanguages", x => new { x.ProfileId, x.LanguageCode });
                    table.ForeignKey(
                        name: "FK_TeacherProfileLanguages_TeacherProfiles_WorkspaceId_ProfileId",
                        columns: x => new { x.WorkspaceId, x.ProfileId },
                        principalTable: "TeacherProfiles",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "TeacherProfileLevels",
                columns: table => new
                {
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LevelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherProfileLevels", x => new { x.ProfileId, x.LevelId });
                    table.ForeignKey(
                        name: "FK_TeacherProfileLevels_EducationLevels_LevelId",
                        column: x => x.LevelId,
                        principalTable: "EducationLevels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TeacherProfileLevels_TeacherProfiles_WorkspaceId_ProfileId",
                        columns: x => new { x.WorkspaceId, x.ProfileId },
                        principalTable: "TeacherProfiles",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "TeacherProfileSubjects",
                columns: table => new
                {
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherProfileSubjects", x => new { x.ProfileId, x.SubjectId });
                    table.ForeignKey(
                        name: "FK_TeacherProfileSubjects_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TeacherProfileSubjects_TeacherProfiles_WorkspaceId_ProfileId",
                        columns: x => new { x.WorkspaceId, x.ProfileId },
                        principalTable: "TeacherProfiles",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                });

            migrationBuilder.InsertData(
                table: "EducationLevels",
                columns: new[] { "Id", "Code", "NameEn", "NameMk", "SortOrder" },
                values: new object[,]
                {
                    { new Guid("6c4d75c8-e8ce-85b2-855d-76a5de06548a"), "primary-upper", "Primary (grades 6–9)", "Основно (6–9 одд.)", 30 },
                    { new Guid("966c9507-81fe-84d8-83da-eaee33bf0590"), "preschool", "Preschool", "Предучилишно", 10 },
                    { new Guid("9b6309c4-477c-8e90-8a3a-0d54e1072b33"), "secondary", "Secondary", "Средно", 40 },
                    { new Guid("ba74857e-c742-8be2-a7ac-2e76cbd34bd1"), "primary-lower", "Primary (grades 1–5)", "Основно (1–5 одд.)", 20 },
                    { new Guid("cef3e47e-3c42-88ec-be62-23fab9fabae8"), "university", "University", "Факултет", 50 },
                    { new Guid("f8a251b8-dc51-84a0-9928-176ee8f36666"), "adults", "Adults", "Возрасни", 60 }
                });

            migrationBuilder.InsertData(
                table: "SubjectCategories",
                columns: new[] { "Id", "Code", "NameEn", "NameMk", "SortOrder" },
                values: new object[,]
                {
                    { new Guid("0db075ef-30e5-8580-be5f-7b3ca817e879"), "exams", "Exam preparation", "Подготовка за испити", 60 },
                    { new Guid("1315844f-76fb-8559-b56d-229e113ea93f"), "social", "Humanities & social sciences", "Општествени науки", 30 },
                    { new Guid("17e6cd67-6a49-8e7f-bf93-696f0aec817c"), "it", "Computing", "Информатика", 40 },
                    { new Guid("1e9da771-d8b2-82d1-9472-14cdae52ce29"), "math-science", "Mathematics & science", "Математика и природни науки", 20 },
                    { new Guid("397ff651-6b09-8299-9d88-c774a8cf0469"), "languages", "Languages", "Јазици", 10 },
                    { new Guid("ea8e5715-d388-84ac-a08b-7be3ec73ccc5"), "arts", "Arts & music", "Уметност и музика", 50 }
                });

            migrationBuilder.InsertData(
                table: "Subjects",
                columns: new[] { "Id", "CategoryId", "Code", "IsActive", "NameEn", "NameMk", "SortOrder" },
                values: new object[,]
                {
                    { new Guid("0d00074d-d9e7-81d3-a0bc-d13c93f0fa34"), new Guid("397ff651-6b09-8299-9d88-c774a8cf0469"), "turkish", true, "Turkish", "Турски јазик", 90 },
                    { new Guid("195f634e-5090-813b-bbd9-2d61eb8f786c"), new Guid("0db075ef-30e5-8580-be5f-7b3ca817e879"), "language-certificates", true, "Language certificates", "Јазични сертификати", 250 },
                    { new Guid("19ec2c1e-a48e-8488-8b97-625280eaf355"), new Guid("1315844f-76fb-8559-b56d-229e113ea93f"), "philosophy", true, "Philosophy", "Филозофија", 160 },
                    { new Guid("31006688-ac16-8b8b-bee9-870edeb892cf"), new Guid("1e9da771-d8b2-82d1-9472-14cdae52ce29"), "biology", true, "Biology", "Биологија", 130 },
                    { new Guid("31b544c6-94e4-8bae-bf7c-56ba892b1de7"), new Guid("397ff651-6b09-8299-9d88-c774a8cf0469"), "french", true, "French", "Француски јазик", 30 },
                    { new Guid("3669c55e-f275-87a0-9cd8-a34178c16196"), new Guid("1e9da771-d8b2-82d1-9472-14cdae52ce29"), "physics", true, "Physics", "Физика", 110 },
                    { new Guid("37a95e21-70b3-8471-b6f4-ce6e4370474a"), new Guid("17e6cd67-6a49-8e7f-bf93-696f0aec817c"), "programming", true, "Programming", "Програмирање", 180 },
                    { new Guid("44136582-6911-86d2-93a3-6313a3781ba5"), new Guid("1e9da771-d8b2-82d1-9472-14cdae52ce29"), "math", true, "Mathematics", "Математика", 100 },
                    { new Guid("44868a03-7772-8927-b212-bccb752ebe01"), new Guid("ea8e5715-d388-84ac-a08b-7be3ec73ccc5"), "art", true, "Visual arts", "Ликовна уметност", 230 },
                    { new Guid("56ce51d5-0549-80d9-9d30-cc5e0d68a8a6"), new Guid("ea8e5715-d388-84ac-a08b-7be3ec73ccc5"), "music", true, "Music", "Музика", 200 },
                    { new Guid("5b7474b5-b568-80c3-8cf3-2f39eafc8e8d"), new Guid("397ff651-6b09-8299-9d88-c774a8cf0469"), "russian", true, "Russian", "Руски јазик", 60 },
                    { new Guid("5cabbe08-fbdd-811b-be9d-3090efd1ad12"), new Guid("1315844f-76fb-8559-b56d-229e113ea93f"), "economics", true, "Economics", "Економија", 170 },
                    { new Guid("66a59e90-fb33-8e6d-a2bb-4ca4402b4689"), new Guid("397ff651-6b09-8299-9d88-c774a8cf0469"), "english", true, "English", "Англиски јазик", 10 },
                    { new Guid("8530ece7-51ee-86c5-9ecc-c0277671356f"), new Guid("17e6cd67-6a49-8e7f-bf93-696f0aec817c"), "computer-basics", true, "Computer basics", "Основи на информатиката", 190 },
                    { new Guid("9581b632-3535-8ea8-9f05-8bd8fc2a3961"), new Guid("0db075ef-30e5-8580-be5f-7b3ca817e879"), "state-matura", true, "State matura", "Државна матура", 240 },
                    { new Guid("9c5fdb7d-2ffa-8b2e-9cf3-3335bb4791ac"), new Guid("ea8e5715-d388-84ac-a08b-7be3ec73ccc5"), "guitar", true, "Guitar", "Гитара", 220 },
                    { new Guid("ad13da06-1670-825b-9fd0-1b8031114c4f"), new Guid("397ff651-6b09-8299-9d88-c774a8cf0469"), "german", true, "German", "Германски јазик", 20 },
                    { new Guid("b7d70e13-7227-8cb3-95b9-7f20f341ec69"), new Guid("397ff651-6b09-8299-9d88-c774a8cf0469"), "macedonian", true, "Macedonian", "Македонски јазик", 70 },
                    { new Guid("ba982c45-97a6-8984-a2c3-b02a98b58cd6"), new Guid("397ff651-6b09-8299-9d88-c774a8cf0469"), "albanian", true, "Albanian", "Албански јазик", 80 },
                    { new Guid("bbf625a2-9ed1-8c29-8c05-f91ea2724a20"), new Guid("1315844f-76fb-8559-b56d-229e113ea93f"), "geography", true, "Geography", "Географија", 140 },
                    { new Guid("bd53e27a-292f-819c-830c-044a87f17a93"), new Guid("1315844f-76fb-8559-b56d-229e113ea93f"), "history", true, "History", "Историја", 150 },
                    { new Guid("db91ddbd-a0ce-8b66-afa0-31ef0ec19d1f"), new Guid("ea8e5715-d388-84ac-a08b-7be3ec73ccc5"), "piano", true, "Piano", "Пијано", 210 },
                    { new Guid("e2df4b0d-4a97-8de4-96b6-9555ade37dd9"), new Guid("397ff651-6b09-8299-9d88-c774a8cf0469"), "spanish", true, "Spanish", "Шпански јазик", 50 },
                    { new Guid("e7ca8aa4-367e-8530-abbc-fa822e0ff2bb"), new Guid("397ff651-6b09-8299-9d88-c774a8cf0469"), "italian", true, "Italian", "Италијански јазик", 40 },
                    { new Guid("e962a711-7192-8d10-be32-bdcb2ce2b979"), new Guid("1e9da771-d8b2-82d1-9472-14cdae52ce29"), "chemistry", true, "Chemistry", "Хемија", 120 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_EducationLevels_Code",
                table: "EducationLevels",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_StorageKey",
                table: "StoredFiles",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_WorkspaceId",
                table: "StoredFiles",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_SubjectCategories_Code",
                table: "SubjectCategories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_CategoryId",
                table: "Subjects",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_Code",
                table: "Subjects",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeacherProfileLanguages_WorkspaceId",
                table: "TeacherProfileLanguages",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherProfileLanguages_WorkspaceId_ProfileId",
                table: "TeacherProfileLanguages",
                columns: new[] { "WorkspaceId", "ProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_TeacherProfileLevels_LevelId",
                table: "TeacherProfileLevels",
                column: "LevelId");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherProfileLevels_WorkspaceId",
                table: "TeacherProfileLevels",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherProfileLevels_WorkspaceId_ProfileId",
                table: "TeacherProfileLevels",
                columns: new[] { "WorkspaceId", "ProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_TeacherProfiles_WorkspaceId",
                table: "TeacherProfiles",
                column: "WorkspaceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeacherProfiles_WorkspaceId_PhotoFileId",
                table: "TeacherProfiles",
                columns: new[] { "WorkspaceId", "PhotoFileId" });

            migrationBuilder.CreateIndex(
                name: "IX_TeacherProfileSubjects_SubjectId",
                table: "TeacherProfileSubjects",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherProfileSubjects_WorkspaceId",
                table: "TeacherProfileSubjects",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherProfileSubjects_WorkspaceId_ProfileId",
                table: "TeacherProfileSubjects",
                columns: new[] { "WorkspaceId", "ProfileId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeacherProfileLanguages");

            migrationBuilder.DropTable(
                name: "TeacherProfileLevels");

            migrationBuilder.DropTable(
                name: "TeacherProfileSubjects");

            migrationBuilder.DropTable(
                name: "EducationLevels");

            migrationBuilder.DropTable(
                name: "Subjects");

            migrationBuilder.DropTable(
                name: "TeacherProfiles");

            migrationBuilder.DropTable(
                name: "SubjectCategories");

            migrationBuilder.DropTable(
                name: "StoredFiles");
        }
    }
}
