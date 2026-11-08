using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.SharedKernel.Tenancy;

namespace ClassHub.Core.Modules.Lessons.Domain;

/// <summary>Self-service booking settings of a workspace (R2-P6). One row per workspace.</summary>
public sealed class BookingSettings : ITenantOwned
{
    public static readonly int[] SlotSteps = [15, 30, 60];
    public const int MaxNoticeHours = 7 * 24;
    public const int MinHorizonDays = 7;
    public const int MaxHorizonDays = 90;

    public Guid WorkspaceId { get; set; }

    /// <summary>Visitors can book on <c>/{slug}/book</c>.</summary>
    public bool Enabled { get; set; }

    /// <summary>Slots start every this many minutes within working hours.</summary>
    public int SlotStepMinutes { get; set; } = 30;

    /// <summary>The earliest bookable slot is this many hours from now.</summary>
    public int MinNoticeHours { get; set; } = 12;

    /// <summary>How many days ahead visitors can book.</summary>
    public int HorizonDays { get; set; } = 30;

    public DateTime UpdatedAtUtc { get; set; }

    public int Version { get; set; }
}

/// <summary>A bookable interval on a weekday, in the workspace time zone (several per day are allowed).</summary>
public sealed class WorkingHours : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public DayOfWeek DayOfWeek { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }
}

/// <summary>Days off (whole local days, inclusive): no slots on them.</summary>
public sealed class TimeOff : ITenantOwned
{
    public const int NoteMaxLength = 120;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public string? Note { get; set; }
}

/// <summary>
/// A visitor's request for a slot (R2-P6). The visitor's contact details and consent are on the linked lead; the offer is
/// recorded as it was requested. Pending requests do not block slots (D-23); confirming re-checks for conflicts and
/// creates the lesson.
/// </summary>
public sealed class BookingRequest : ITenantOwned
{
    public const int ReasonMaxLength = 500;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid LeadId { get; set; }

    public Guid OfferId { get; set; }

    public required string OfferTitle { get; set; }

    public Guid? SubjectId { get; set; }

    public LessonMode Mode { get; set; }

    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }

    public required string TimeZone { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Pending;

    public Guid? LessonId { get; set; }

    public string? Reason { get; set; }

    public DateTime? DecidedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public int Version { get; set; }
}

/// <summary>A bookable slot: its local day and time and its instant.</summary>
public sealed record BookableSlot(DateOnly Date, TimeOnly Time, DateTime StartUtc);

/// <summary>
/// Free slots (R2-P6): every <c>step</c> minutes within working hours, where the whole lesson fits, outside days off, not
/// sooner than the notice and not later than the horizon, and not overlapping a busy interval (touching is fine).
/// Local times that do not exist (clocks forward) give no slot; a repeated local time means its first occurrence.
/// </summary>
public static class BookingSlots
{
    public static IReadOnlyList<BookableSlot> Compute(
        IReadOnlyList<WorkingHours> hours,
        IReadOnlyList<TimeOff> timeOff,
        IReadOnlyList<(DateTime StartUtc, DateTime EndUtc)> busy,
        TimeZoneInfo zone,
        DateTime nowUtc,
        int durationMinutes,
        int stepMinutes,
        int minNoticeHours,
        int horizonDays,
        DateOnly from,
        DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var today = DateOnly.FromDateTime(LessonTime.ToLocal(nowUtc, zone));
        var first = from > today ? from : today;
        var last = today.AddDays(horizonDays - 1) < to ? today.AddDays(horizonDays - 1) : to;
        var earliest = nowUtc.AddHours(minNoticeHours);
        var slots = new List<BookableSlot>();
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            if (timeOff.Any(o => o.StartDate <= day && day <= o.EndDate))
            {
                continue;
            }

            foreach (var interval in hours.Where(h => h.DayOfWeek == day.DayOfWeek).OrderBy(h => h.StartTime))
            {
                var startMinute = interval.StartTime.Hour * 60 + interval.StartTime.Minute;
                var endMinute = interval.EndTime.Hour * 60 + interval.EndTime.Minute;
                for (var minute = startMinute; minute + durationMinutes <= endMinute; minute += stepMinutes)
                {
                    var time = new TimeOnly(minute / 60, minute % 60);
                    var local = day.ToDateTime(time, DateTimeKind.Unspecified);
                    if (zone.IsInvalidTime(local))
                    {
                        continue;
                    }

                    var start = LessonTime.ToUtc(local, zone, "startUtc");
                    var end = start.AddMinutes(durationMinutes);
                    if (start >= earliest && !busy.Any(b => LessonRules.Overlaps(start, end, b.StartUtc, b.EndUtc)) && !slots.Any(s => s.StartUtc == start))
                    {
                        slots.Add(new BookableSlot(day, time, start));
                    }
                }
            }
        }

        return slots;
    }
}
