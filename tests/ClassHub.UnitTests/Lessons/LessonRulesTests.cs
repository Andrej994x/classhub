using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Lessons.Domain;
using ClassHub.Core.SharedKernel.Errors;

namespace ClassHub.UnitTests.Lessons;

/// <summary>R2-P4: the lesson state machine (D-22), the overlap rule and wall-clock times across daylight-saving changes.</summary>
public sealed class LessonRulesTests
{
    private static readonly TimeZoneInfo Skopje = TimeZoneInfo.FindSystemTimeZoneById("Europe/Skopje");

    [Theory]
    [InlineData(LessonStatus.Scheduled, LessonStatus.Completed, true)]
    [InlineData(LessonStatus.Scheduled, LessonStatus.Cancelled, true)]
    [InlineData(LessonStatus.Completed, LessonStatus.Cancelled, false)]
    [InlineData(LessonStatus.Completed, LessonStatus.Completed, false)]
    [InlineData(LessonStatus.Cancelled, LessonStatus.Completed, false)]
    [InlineData(LessonStatus.Cancelled, LessonStatus.Scheduled, false)]
    [InlineData(LessonStatus.Completed, LessonStatus.Scheduled, false)]
    public void Only_a_scheduled_lesson_moves_and_only_to_a_final_state(LessonStatus from, LessonStatus to, bool allowed) =>
        LessonRules.CanChange(from, to).ShouldBe(allowed);

    [Theory]
    [InlineData(10, 11, 11, 12, false)] // touching: one ends when the next starts
    [InlineData(11, 12, 10, 11, false)]
    [InlineData(10, 12, 11, 13, true)]
    [InlineData(10, 13, 11, 12, true)] // containment
    [InlineData(11, 12, 10, 13, true)]
    [InlineData(10, 11, 10, 11, true)] // same time
    [InlineData(8, 9, 10, 11, false)]
    public void Intervals_overlap_only_when_they_share_time(int startA, int endA, int startB, int endB, bool overlap)
    {
        var day = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
        LessonRules.Overlaps(day.AddHours(startA), day.AddHours(endA), day.AddHours(startB), day.AddHours(endB)).ShouldBe(overlap);
    }

    [Fact]
    public void Cancelled_lessons_free_their_time()
    {
        LessonRules.OccupiesSchedule(LessonStatus.Cancelled).ShouldBeFalse();
        LessonRules.OccupiesSchedule(LessonStatus.Scheduled).ShouldBeTrue();
        LessonRules.OccupiesSchedule(LessonStatus.Completed).ShouldBeTrue();
    }

    [Fact]
    public void Local_times_convert_with_the_season_offset()
    {
        LessonTime.ToUtc(new DateTime(2026, 11, 10, 17, 0, 0), Skopje, "startLocal").ShouldBe(new DateTime(2026, 11, 10, 16, 0, 0, DateTimeKind.Utc)); // CET
        LessonTime.ToUtc(new DateTime(2026, 6, 10, 17, 0, 0), Skopje, "startLocal").ShouldBe(new DateTime(2026, 6, 10, 15, 0, 0, DateTimeKind.Utc)); // CEST
        LessonTime.ToLocal(new DateTime(2026, 6, 10, 15, 0, 0, DateTimeKind.Utc), Skopje).ShouldBe(new DateTime(2026, 6, 10, 17, 0, 0));
    }

    [Fact]
    public void A_time_skipped_when_the_clocks_go_forward_is_refused()
    {
        var error = Should.Throw<RequestValidationException>(() => LessonTime.ToUtc(new DateTime(2026, 3, 29, 2, 30, 0), Skopje, "startLocal"));
        error.Errors.ShouldContainKey("startLocal");
    }

    [Fact]
    public void A_time_that_occurs_twice_when_the_clocks_go_back_means_the_first_one()
    {
        // 02:30 happens at 00:30 UTC (summer time) and again at 01:30 UTC; the lesson takes the first.
        LessonTime.ToUtc(new DateTime(2026, 10, 25, 2, 30, 0), Skopje, "startLocal").ShouldBe(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void A_day_that_changes_clocks_has_its_real_length()
    {
        var spring = LessonTime.StartOfDayUtc(new DateOnly(2026, 3, 30), Skopje) - LessonTime.StartOfDayUtc(new DateOnly(2026, 3, 29), Skopje);
        var autumn = LessonTime.StartOfDayUtc(new DateOnly(2026, 10, 26), Skopje) - LessonTime.StartOfDayUtc(new DateOnly(2026, 10, 25), Skopje);
        spring.ShouldBe(TimeSpan.FromHours(23));
        autumn.ShouldBe(TimeSpan.FromHours(25));
    }
}
