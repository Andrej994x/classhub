using ClassHub.Core.Modules.Notifications.Domain;

namespace ClassHub.UnitTests.Notifications;

/// <summary>R2-P11: dedupe keys, reminder timing, preferences and zone-aware lesson times.</summary>
public sealed class NotificationRulesTests
{
    private static readonly Guid Lesson = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid Student = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly DateTime Start = new(2026, 11, 20, 16, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Keys_tell_changes_apart_and_repeat_for_the_same_change()
    {
        NotificationRules.LessonKey(NotificationKind.LessonScheduled, Lesson, Student, 0)
            .ShouldBe("lesson-scheduled:11111111000000000000000000000001:22222222000000000000000000000002:r0");
        // The same change twice: one key. A later change of the lesson (a student added back, a lesson moved back): a new key.
        NotificationRules.LessonKey(NotificationKind.LessonScheduled, Lesson, Student, 3).ShouldBe(NotificationRules.LessonKey(NotificationKind.LessonScheduled, Lesson, Student, 3));
        NotificationRules.LessonKey(NotificationKind.LessonScheduled, Lesson, Student, 4).ShouldNotBe(NotificationRules.LessonKey(NotificationKind.LessonScheduled, Lesson, Student, 3));
        NotificationRules.LessonKey(NotificationKind.LessonCancelled, Lesson, Student, 3).ShouldNotBe(NotificationRules.LessonKey(NotificationKind.LessonScheduled, Lesson, Student, 3));
        NotificationRules.LessonKey(NotificationKind.SeriesScheduled, Lesson, Student, 0).ShouldStartWith("series-scheduled:");

        // Reminders: per start time, and a new attempt after a withdrawn one.
        NotificationRules.ReminderKey(Lesson, Student, Start, 0).ShouldBe(NotificationRules.ReminderKey(Lesson, Student, Start, 0));
        NotificationRules.ReminderKey(Lesson, Student, Start.AddHours(2), 0).ShouldNotBe(NotificationRules.ReminderKey(Lesson, Student, Start, 0));
        NotificationRules.ReminderKey(Lesson, Student, Start, 1).ShouldNotBe(NotificationRules.ReminderKey(Lesson, Student, Start, 0));
        NotificationRules.ReminderKey(Lesson, Guid.NewGuid(), Start, 0).ShouldNotBe(NotificationRules.ReminderKey(Lesson, Student, Start, 0));
        NotificationRules.ReminderKey(Lesson, Student, Start, int.MaxValue).Length.ShouldBeLessThanOrEqualTo(Notification.DedupeKeyMaxLength);

        // New homework: one per assignment.
        var assignment = Guid.NewGuid();
        NotificationRules.HomeworkKey(assignment).ShouldBe(NotificationRules.HomeworkKey(assignment));
        NotificationRules.HomeworkKey(Guid.NewGuid()).ShouldNotBe(NotificationRules.HomeworkKey(assignment));
    }

    [Fact]
    public void Reminders_are_due_a_day_before_or_soon_after_a_late_booking_but_never_at_the_last_minute()
    {
        NotificationRules.ReminderDueAtUtc(Start, Start.AddDays(-3)).ShouldBe(Start.AddHours(-24));
        NotificationRules.ReminderDueAtUtc(Start, Start.AddHours(-24)).ShouldBe(Start.AddHours(-24));
        var late = Start.AddHours(-5);
        NotificationRules.ReminderDueAtUtc(Start, late).ShouldBe(late); // planned within the day: right away
        NotificationRules.ReminderDueAtUtc(Start, Start.AddMinutes(-59)).ShouldBeNull(); // less than an hour before: none
        NotificationRules.ReminderDueAtUtc(Start, Start.AddHours(1)).ShouldBeNull(); // already started
    }

    [Fact]
    public void Only_reminders_and_new_homework_can_be_switched_off()
    {
        var off = new NotificationPreference { LessonReminders = false, HomeworkEmails = false };
        NotificationRules.Wanted(NotificationKind.LessonReminder, off).ShouldBeFalse();
        NotificationRules.Wanted(NotificationKind.HomeworkAssigned, off).ShouldBeFalse();
        NotificationRules.Wanted(NotificationKind.LessonCancelled, off).ShouldBeTrue();
        NotificationRules.Wanted(NotificationKind.LessonScheduled, off).ShouldBeTrue();
        NotificationRules.Wanted(NotificationKind.LessonReminder, null).ShouldBeTrue(); // no choice saved: on
        NotificationRules.IsOptional(NotificationKind.LessonRescheduled).ShouldBeFalse();
    }

    [Fact]
    public void Lesson_times_are_local_to_the_lessons_zone_and_name_it()
    {
        // Winter (UTC+1) and summer (UTC+2) in Skopje; the same instant in another zone.
        NotificationRules.LocalTime(new DateTime(2026, 11, 20, 16, 0, 0, DateTimeKind.Utc), "Europe/Skopje", "en")
            .ShouldBe("Friday, 20 November 2026, 17:00 (Europe/Skopje)");
        NotificationRules.LocalTime(new DateTime(2026, 7, 3, 16, 0, 0, DateTimeKind.Utc), "Europe/Skopje", "en")
            .ShouldBe("Friday, 3 July 2026, 18:00 (Europe/Skopje)");
        NotificationRules.LocalTime(new DateTime(2026, 11, 20, 16, 0, 0, DateTimeKind.Utc), "America/New_York", "en")
            .ShouldBe("Friday, 20 November 2026, 11:00 (America/New_York)");
        NotificationRules.LocalTime(new DateTime(2026, 11, 20, 16, 0, 0, DateTimeKind.Utc), "Europe/Skopje", "mk").ShouldContain("17:00");
        NotificationRules.LocalTime(new DateTime(2026, 11, 20, 16, 0, 0, DateTimeKind.Utc), "Europe/Skopje", "mk").ShouldContain("2026");
    }
}
