using ClassHub.Core.Modules.Homework.Domain;
using ClassHub.Core.SharedKernel.Errors;

namespace ClassHub.UnitTests.Homework;

/// <summary>R2-P9: Assigned → Submitted → Reviewed, resubmission until the review (D-41), late flags and the due instant.</summary>
public sealed class HomeworkRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void An_assigned_homework_is_submitted_and_can_be_resubmitted_until_the_review()
    {
        var assignment = new HomeworkAssignment();

        HomeworkRules.Submit(assignment, Now);
        assignment.Status.ShouldBe(HomeworkStatus.Submitted);
        assignment.SubmittedAtUtc.ShouldBe(Now);

        HomeworkRules.Submit(assignment, Now.AddHours(1));
        assignment.Status.ShouldBe(HomeworkStatus.Submitted);
        assignment.SubmittedAtUtc.ShouldBe(Now.AddHours(1)); // the latest answer counts
    }

    [Fact]
    public void Reviewing_needs_a_submission_and_makes_the_homework_final()
    {
        var assignment = new HomeworkAssignment();
        Should.Throw<BusinessRuleException>(() => HomeworkRules.Review(assignment, "Браво", Now)).Code.ShouldBe(ErrorCodes.InvalidStatusTransition);

        HomeworkRules.Submit(assignment, Now);
        HomeworkRules.Review(assignment, "  Браво!  ", Now.AddDays(1));

        assignment.Status.ShouldBe(HomeworkStatus.Reviewed);
        assignment.ReviewedAtUtc.ShouldBe(Now.AddDays(1));
        assignment.TeacherComment.ShouldBe("Браво!");
        Should.Throw<BusinessRuleException>(() => HomeworkRules.Submit(assignment, Now.AddDays(2))).Code.ShouldBe(ErrorCodes.InvalidStatusTransition);
        assignment.SubmittedAtUtc.ShouldBe(Now);
    }

    [Fact]
    public void Changing_the_comment_of_a_reviewed_homework_keeps_the_review_time()
    {
        var assignment = new HomeworkAssignment();
        HomeworkRules.Submit(assignment, Now);
        HomeworkRules.Review(assignment, "Прво", Now.AddDays(1));

        HomeworkRules.Review(assignment, "   ", Now.AddDays(2));

        assignment.ReviewedAtUtc.ShouldBe(Now.AddDays(1));
        assignment.TeacherComment.ShouldBeNull();
    }

    [Fact]
    public void Answers_after_the_end_of_the_due_day_are_late()
    {
        var due = new DateTime(2026, 11, 10, 23, 0, 0, DateTimeKind.Utc);

        HomeworkRules.IsLate(due, due).ShouldBeFalse();
        HomeworkRules.IsLate(due.AddSeconds(1), due).ShouldBeTrue();
        HomeworkRules.IsLate(due.AddDays(5), null).ShouldBeFalse(); // no due date: never late
        HomeworkRules.IsLate(null, due).ShouldBeFalse(); // not submitted
    }

    [Fact]
    public void The_due_day_ends_at_the_next_local_midnight_also_across_clock_changes()
    {
        var skopje = TimeZoneInfo.FindSystemTimeZoneById("Europe/Skopje");

        HomeworkRules.DueAtUtc(new DateOnly(2026, 11, 10), skopje).ShouldBe(new DateTime(2026, 11, 10, 23, 0, 0, DateTimeKind.Utc)); // winter, UTC+1
        HomeworkRules.DueAtUtc(new DateOnly(2026, 7, 1), skopje).ShouldBe(new DateTime(2026, 7, 1, 22, 0, 0, DateTimeKind.Utc)); // summer, UTC+2
        HomeworkRules.DueAtUtc(new DateOnly(2026, 3, 28), skopje).ShouldBe(new DateTime(2026, 3, 28, 23, 0, 0, DateTimeKind.Utc)); // the night before the change

        // Where midnight itself is skipped (Santiago, spring forward at 00:00): the first existing local minute.
        var santiago = TimeZoneInfo.FindSystemTimeZoneById("America/Santiago");
        var due = HomeworkRules.DueAtUtc(new DateOnly(2026, 9, 5), santiago);
        TimeZoneInfo.ConvertTimeFromUtc(due, santiago).ShouldBe(new DateTime(2026, 9, 6, 1, 0, 0));
    }
}
