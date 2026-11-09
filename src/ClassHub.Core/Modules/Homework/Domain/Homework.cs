using ClassHub.Core.Infrastructure.Files;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Tenancy;

namespace ClassHub.Core.Modules.Homework.Domain;

/// <summary>
/// A homework task (R2-P9): title, description, an optional due date (in the workspace's time zone) and an optional
/// lesson it belongs to. Students get it through per-student <see cref="HomeworkAssignment"/>s.
/// </summary>
public sealed class HomeworkTask : ITenantOwned, IArchivable
{
    public const int TitleMaxLength = 160;
    public const int DescriptionMaxLength = 4000;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    /// <summary>The last day to hand in (local to the workspace).</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>The end of <see cref="DueDate"/> in the workspace's time zone when it was set: later is late.</summary>
    public DateTime? DueAtUtc { get; set; }

    public Guid? LessonId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? ArchivedAtUtc { get; set; }

    public int Version { get; set; }
}

public enum HomeworkStatus
{
    Assigned = 1,
    Submitted = 2,
    Reviewed = 3,
}

/// <summary>
/// One student's copy of a homework: status, the current answer (text and/or one file) and the teacher's review.
/// </summary>
public sealed class HomeworkAssignment : ITenantOwned
{
    public const int AnswerMaxLength = 8000;
    public const int CommentMaxLength = 4000;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid HomeworkId { get; set; }

    public Guid StudentId { get; set; }

    public HomeworkStatus Status { get; set; } = HomeworkStatus.Assigned;

    public DateTime AssignedAtUtc { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }

    public string? AnswerText { get; set; }

    /// <summary>The answer's file (<c>StoredFiles</c>, purpose <see cref="HomeworkFiles.Purpose"/>).</summary>
    public Guid? FileId { get; set; }

    public string? FileName { get; set; }

    public string? ContentType { get; set; }

    public long? SizeBytes { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    public string? TeacherComment { get; set; }

    public int Version { get; set; }
}

public static class HomeworkFiles
{
    public const string Purpose = "homework-submission";

    public const int FileNameMaxLength = DocumentFiles.FileNameMaxLength;
}

/// <summary>The homework rules (D-41): submit and resubmit until reviewed, review only what was submitted, late flags.</summary>
public static class HomeworkRules
{
    /// <summary>Records an answer: allowed while the homework is assigned or submitted, never after the review.</summary>
    public static void Submit(HomeworkAssignment assignment, DateTime nowUtc)
    {
        EnsureOpen(assignment);
        assignment.Status = HomeworkStatus.Submitted;
        assignment.SubmittedAtUtc = nowUtc;
    }

    /// <summary>A reviewed homework is final: no further answers (D-41).</summary>
    public static void EnsureOpen(HomeworkAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        if (assignment.Status == HomeworkStatus.Reviewed)
        {
            throw new BusinessRuleException("This homework was already reviewed; it cannot be submitted again.", ErrorCodes.InvalidStatusTransition);
        }
    }

    /// <summary>Reviews a submitted answer (or changes the comment of a reviewed one); nothing to review before a submission.</summary>
    public static void Review(HomeworkAssignment assignment, string? comment, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        if (assignment.Status == HomeworkStatus.Assigned)
        {
            throw new BusinessRuleException("The student has not submitted this homework yet.", ErrorCodes.InvalidStatusTransition);
        }

        assignment.Status = HomeworkStatus.Reviewed;
        assignment.ReviewedAtUtc ??= nowUtc;
        assignment.TeacherComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
    }

    /// <summary>Late: submitted after the end of the due day (homework without a due date is never late).</summary>
    public static bool IsLate(DateTime? submittedAtUtc, DateTime? dueAtUtc) =>
        submittedAtUtc is { } submitted && dueAtUtc is { } due && submitted > due;

    /// <summary>
    /// The end of <paramref name="dueDate"/> in <paramref name="zone"/>: the start of the next local day (the first
    /// existing minute when that midnight is skipped by a clock change).
    /// </summary>
    public static DateTime DueAtUtc(DateOnly dueDate, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = dueDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(15);
        }

        if (zone.IsAmbiguousTime(local))
        {
            var offset = zone.GetAmbiguousTimeOffsets(local).Max();
            return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}
