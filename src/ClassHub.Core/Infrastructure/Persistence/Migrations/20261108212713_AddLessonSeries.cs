using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLessonSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SeriesException",
                table: "Lessons",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "SeriesId",
                table: "Lessons",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeriesIndex",
                table: "Lessons",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LessonSeries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TeacherUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Topic = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    MeetingUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TimeZone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    IntervalWeeks = table.Column<int>(type: "int", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Count = table.Column<int>(type: "int", nullable: true),
                    NextIndex = table.Column<int>(type: "int", nullable: false),
                    ReplacedBySeriesId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonSeries", x => x.Id);
                    table.UniqueConstraint("AK_LessonSeries_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_LessonSeries_Count", "[Count] IS NULL OR [Count] BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_LessonSeries_Duration", "[DurationMinutes] BETWEEN 15 AND 480");
                    table.CheckConstraint("CK_LessonSeries_Interval", "[IntervalWeeks] IN (1, 2)");
                    table.CheckConstraint("CK_LessonSeries_Kind", "([Kind] = 1 AND [StudentId] IS NOT NULL AND [GroupId] IS NULL) OR ([Kind] = 2 AND [GroupId] IS NOT NULL AND [StudentId] IS NULL)");
                    table.CheckConstraint("CK_LessonSeries_Mode", "[Mode] IN (1, 2)");
                    table.CheckConstraint("CK_LessonSeries_Title", "LEN([Title]) >= 1");
                    table.ForeignKey(
                        name: "FK_LessonSeries_AspNetUsers_TeacherUserId",
                        column: x => x.TeacherUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LessonSeries_Groups_WorkspaceId_GroupId",
                        columns: x => new { x.WorkspaceId, x.GroupId },
                        principalTable: "Groups",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                    table.ForeignKey(
                        name: "FK_LessonSeries_LessonSeries_WorkspaceId_ReplacedBySeriesId",
                        columns: x => new { x.WorkspaceId, x.ReplacedBySeriesId },
                        principalTable: "LessonSeries",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                    table.ForeignKey(
                        name: "FK_LessonSeries_Students_WorkspaceId_StudentId",
                        columns: x => new { x.WorkspaceId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                    table.ForeignKey(
                        name: "FK_LessonSeries_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LessonSeries_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LessonSeriesSkips",
                columns: table => new
                {
                    SeriesId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeriesIndex = table.Column<int>(type: "int", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonSeriesSkips", x => new { x.SeriesId, x.SeriesIndex });
                    table.CheckConstraint("CK_LessonSeriesSkips_Reason", "[Reason] BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "FK_LessonSeriesSkips_LessonSeries_WorkspaceId_SeriesId",
                        columns: x => new { x.WorkspaceId, x.SeriesId },
                        principalTable: "LessonSeries",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_SeriesId_SeriesIndex",
                table: "Lessons",
                columns: new[] { "SeriesId", "SeriesIndex" },
                unique: true,
                filter: "[SeriesId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_WorkspaceId_SeriesId",
                table: "Lessons",
                columns: new[] { "WorkspaceId", "SeriesId" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonSeries_ReplacedBySeriesId_EndDate",
                table: "LessonSeries",
                columns: new[] { "ReplacedBySeriesId", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonSeries_SubjectId",
                table: "LessonSeries",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonSeries_TeacherUserId",
                table: "LessonSeries",
                column: "TeacherUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonSeries_WorkspaceId",
                table: "LessonSeries",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonSeries_WorkspaceId_GroupId",
                table: "LessonSeries",
                columns: new[] { "WorkspaceId", "GroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonSeries_WorkspaceId_ReplacedBySeriesId",
                table: "LessonSeries",
                columns: new[] { "WorkspaceId", "ReplacedBySeriesId" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonSeries_WorkspaceId_StudentId",
                table: "LessonSeries",
                columns: new[] { "WorkspaceId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonSeriesSkips_WorkspaceId",
                table: "LessonSeriesSkips",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonSeriesSkips_WorkspaceId_SeriesId",
                table: "LessonSeriesSkips",
                columns: new[] { "WorkspaceId", "SeriesId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Lessons_LessonSeries_WorkspaceId_SeriesId",
                table: "Lessons",
                columns: new[] { "WorkspaceId", "SeriesId" },
                principalTable: "LessonSeries",
                principalColumns: new[] { "WorkspaceId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Lessons_LessonSeries_WorkspaceId_SeriesId",
                table: "Lessons");

            migrationBuilder.DropTable(
                name: "LessonSeriesSkips");

            migrationBuilder.DropTable(
                name: "LessonSeries");

            migrationBuilder.DropIndex(
                name: "IX_Lessons_SeriesId_SeriesIndex",
                table: "Lessons");

            migrationBuilder.DropIndex(
                name: "IX_Lessons_WorkspaceId_SeriesId",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "SeriesException",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "SeriesIndex",
                table: "Lessons");
        }
    }
}
