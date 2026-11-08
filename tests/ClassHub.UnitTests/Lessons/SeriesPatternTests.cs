using ClassHub.Core.Modules.Lessons.Domain;

namespace ClassHub.UnitTests.Lessons;

/// <summary>R2-P5: expanding a series into occurrences (intervals, ends, daylight-saving changes).</summary>
public sealed class SeriesPatternTests
{
    private static readonly TimeZoneInfo Skopje = TimeZoneInfo.FindSystemTimeZoneById("Europe/Skopje");
    private static readonly DateOnly Tuesday = new(2030, 10, 15);

    [Fact]
    public void Weekly_and_biweekly_occurrences_fall_on_the_same_weekday()
    {
        var weekly = SeriesPattern.Occurrences(Tuesday, new TimeOnly(17, 0), 1, null, null, 0, Tuesday.AddDays(28), Skopje).Select(o => o.Date).ToList();
        var biweekly = SeriesPattern.Occurrences(Tuesday, new TimeOnly(17, 0), 2, null, null, 0, Tuesday.AddDays(42), Skopje).Select(o => o.Date).ToList();

        weekly.ShouldBe([Tuesday, Tuesday.AddDays(7), Tuesday.AddDays(14), Tuesday.AddDays(21)]);
        biweekly.ShouldBe([Tuesday, Tuesday.AddDays(14), Tuesday.AddDays(28)]);
        weekly.ShouldAllBe(d => d.DayOfWeek == DayOfWeek.Tuesday);
    }

    [Fact]
    public void A_series_ends_after_a_count_or_on_a_date()
    {
        SeriesPattern.Occurrences(Tuesday, new TimeOnly(17, 0), 1, null, 3, 0, Tuesday.AddDays(365), Skopje).Count().ShouldBe(3);
        SeriesPattern.Occurrences(Tuesday, new TimeOnly(17, 0), 1, Tuesday.AddDays(14), null, 0, Tuesday.AddDays(365), Skopje).Count().ShouldBe(3); // end date included
        SeriesPattern.Occurrences(Tuesday, new TimeOnly(17, 0), 1, null, 10, 8, Tuesday.AddDays(365), Skopje).Select(o => o.Index).ShouldBe([8, 9]);
        SeriesPattern.Includes(Tuesday, 1, null, 10, 10).ShouldBeFalse();
    }

    [Fact]
    public void The_local_time_stays_the_same_across_the_clock_change()
    {
        // 17:00 on 22 October 2030 is summer time (15:00 UTC); after the change on 27 October it is 16:00 UTC.
        var occurrences = SeriesPattern.Occurrences(new DateOnly(2030, 10, 22), new TimeOnly(17, 0), 1, null, 2, 0, new DateOnly(2031, 1, 1), Skopje).ToList();

        occurrences[0].StartUtc.ShouldBe(new DateTime(2030, 10, 22, 15, 0, 0, DateTimeKind.Utc));
        occurrences[1].StartUtc.ShouldBe(new DateTime(2030, 10, 29, 16, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void An_occurrence_at_a_skipped_time_has_no_instant()
    {
        // Sundays at 02:30: on 31 March 2030 that time does not exist in Skopje.
        var occurrences = SeriesPattern.Occurrences(new DateOnly(2030, 3, 24), new TimeOnly(2, 30), 1, null, 3, 0, new DateOnly(2031, 1, 1), Skopje).ToList();

        occurrences[0].StartUtc.ShouldNotBeNull();
        occurrences[1].Date.ShouldBe(new DateOnly(2030, 3, 31));
        occurrences[1].StartUtc.ShouldBeNull();
        occurrences[2].StartUtc.ShouldNotBeNull();
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-3, 0)]
    [InlineData(1, 1)]
    [InlineData(7, 1)]
    [InlineData(8, 2)]
    public void The_first_occurrence_on_or_after_a_date(int daysAfterStart, int expected) =>
        SeriesPattern.FirstIndexOnOrAfter(Tuesday, 1, Tuesday.AddDays(daysAfterStart)).ShouldBe(expected);
}
