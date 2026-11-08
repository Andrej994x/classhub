using ClassHub.Core.Modules.Lessons.Domain;

namespace ClassHub.UnitTests.Lessons;

/// <summary>R2-P6: free slots from working hours, days off, busy lessons, notice and horizon.</summary>
public sealed class BookingSlotsTests
{
    private static readonly TimeZoneInfo Skopje = TimeZoneInfo.FindSystemTimeZoneById("Europe/Skopje");

    // Monday 4 November 2030, 08:00 in Skopje (07:00 UTC).
    private static readonly DateTime Now = new(2030, 11, 4, 7, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Monday = new(2030, 11, 4);

    private static WorkingHours Hours(DayOfWeek day, int from, int to) => new() { DayOfWeek = day, StartTime = new TimeOnly(from, 0), EndTime = new TimeOnly(to, 0) };

    private static IReadOnlyList<BookableSlot> Slots(
        IReadOnlyList<WorkingHours> hours,
        IReadOnlyList<(DateTime, DateTime)>? busy = null,
        IReadOnlyList<TimeOff>? off = null,
        int duration = 60,
        int step = 30,
        int notice = 0,
        int horizon = 30,
        DateOnly? from = null,
        DateOnly? to = null,
        DateTime? now = null) =>
        BookingSlots.Compute(hours, off ?? [], busy ?? [], Skopje, now ?? Now, duration, step, notice, horizon, from ?? Monday, to ?? (from ?? Monday));

    [Fact]
    public void Slots_start_every_step_and_the_whole_lesson_fits_the_working_hours()
    {
        var slots = Slots([Hours(DayOfWeek.Monday, 16, 18)]);

        slots.Select(s => s.Time.ToString("HH:mm")).ShouldBe(["16:00", "16:30", "17:00"]);
        slots[0].StartUtc.ShouldBe(new DateTime(2030, 11, 4, 15, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Busy_lessons_remove_overlapping_slots_but_touching_ones_stay()
    {
        var busy = (new DateTime(2030, 11, 4, 15, 30, 0, DateTimeKind.Utc), new DateTime(2030, 11, 4, 16, 0, 0, DateTimeKind.Utc)); // 16:30–17:00 local

        Slots([Hours(DayOfWeek.Monday, 16, 19)], busy: [busy]).Select(s => s.Time.ToString("HH:mm")).ShouldBe(["17:00", "17:30", "18:00"]);
    }

    [Fact]
    public void Notice_days_off_and_other_weekdays_give_no_slots()
    {
        Slots([Hours(DayOfWeek.Monday, 9, 12)], notice: 3).Select(s => s.Time.ToString("HH:mm")).ShouldBe(["11:00"]); // from 11:00 local
        Slots([Hours(DayOfWeek.Monday, 9, 12)], off: [new TimeOff { StartDate = Monday, EndDate = Monday }]).ShouldBeEmpty();
        Slots([Hours(DayOfWeek.Tuesday, 9, 12)]).ShouldBeEmpty();
    }

    [Fact]
    public void The_past_and_days_beyond_the_horizon_give_no_slots()
    {
        Slots([Hours(DayOfWeek.Monday, 6, 9)]).Select(s => s.Time.ToString("HH:mm")).ShouldBe(["08:00"]); // 06:00–07:30 are past
        Slots([Hours(DayOfWeek.Monday, 9, 10)], from: Monday.AddDays(-7), to: Monday.AddDays(14), horizon: 8).Select(s => s.Date).ShouldBe([Monday, Monday.AddDays(7)]);
    }

    [Fact]
    public void Several_intervals_a_day_and_several_days()
    {
        var slots = Slots([Hours(DayOfWeek.Monday, 9, 10), Hours(DayOfWeek.Monday, 15, 16), Hours(DayOfWeek.Wednesday, 9, 10)], step: 60, to: Monday.AddDays(6));

        slots.Select(s => $"{s.Date.DayOfWeek} {s.Time:HH:mm}").ShouldBe(["Monday 09:00", "Monday 15:00", "Wednesday 09:00"]);
    }

    [Fact]
    public void The_clock_change_moves_the_instant_but_keeps_the_local_time()
    {
        var sunday = new DateOnly(2030, 3, 31); // clocks jump 02:00 → 03:00
        var slots = Slots([Hours(DayOfWeek.Sunday, 1, 4)], step: 30, duration: 30, from: sunday, to: sunday, now: new DateTime(2030, 3, 30, 0, 0, 0, DateTimeKind.Utc));

        slots.Select(s => s.Time.ToString("HH:mm")).ShouldBe(["01:00", "01:30", "03:00", "03:30"]); // 02:00 and 02:30 do not exist
        slots[2].StartUtc.ShouldBe(new DateTime(2030, 3, 31, 1, 0, 0, DateTimeKind.Utc));
    }
}
