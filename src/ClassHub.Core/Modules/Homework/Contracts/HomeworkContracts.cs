namespace ClassHub.Core.Modules.Homework.Contracts;

/// <summary>An active assignment as a notification shows it (title and due date).</summary>
public sealed record HomeworkNotice(Guid HomeworkId, Guid AssignmentId, string Title, DateOnly? DueDate);

/// <summary>Homework of the current workspace, for other modules.</summary>
public interface IHomeworkNotices
{
    /// <summary>The student's assignment of the homework while both are active (not archived, not removed); otherwise null.</summary>
    Task<HomeworkNotice?> FindAsync(Guid homeworkId, Guid studentId, CancellationToken cancellationToken);
}

/// <summary>Create or change a homework; the version is the loaded one (null when creating).</summary>
public sealed record SaveHomeworkRequest(string? Title, string? Description, DateOnly? DueDate, Guid? LessonId, int? Version);

public sealed record HomeworkVersionRequest(int? Version);

/// <summary>Assign to students and/or the current members of a group (archived students are skipped in a group).</summary>
public sealed record AssignHomeworkRequest(IReadOnlyList<Guid>? StudentIds, Guid? GroupId);

/// <summary>The review: the comment (optional) and the assignment version the teacher looked at.</summary>
public sealed record ReviewHomeworkRequest(string? Comment, int? Version);

/// <summary>The lesson a homework belongs to.</summary>
public sealed record HomeworkLessonResponse(Guid Id, string Title, DateTime StartUtc, string TimeZone);

/// <summary>The answer's file (download through the file endpoint).</summary>
public sealed record HomeworkFileResponse(string FileName, string ContentType, long SizeBytes);

/// <summary>A student's assignment as the teacher sees it. <c>Status</c>: Assigned, Submitted or Reviewed.</summary>
public sealed record HomeworkAssignmentResponse(
    Guid Id,
    Guid StudentId,
    string StudentName,
    bool StudentArchived,
    string Status,
    DateTime AssignedAtUtc,
    DateTime? SubmittedAtUtc,
    bool Late,
    string? AnswerText,
    HomeworkFileResponse? File,
    DateTime? ReviewedAtUtc,
    string? TeacherComment,
    int Version);

public sealed record HomeworkResponse(
    Guid Id,
    string Title,
    string? Description,
    DateOnly? DueDate,
    DateTime? DueAtUtc,
    HomeworkLessonResponse? Lesson,
    bool Archived,
    IReadOnlyList<HomeworkAssignmentResponse> Assignments,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int Version);

/// <summary>A homework in the list with its counts per status.</summary>
public sealed record HomeworkListItem(Guid Id, string Title, DateOnly? DueDate, Guid? LessonId, int Assigned, int Submitted, int Reviewed, bool Archived, DateTime UpdatedAtUtc);

public sealed record HomeworkListResponse(IReadOnlyList<HomeworkListItem> Items, int Total, int Page, int PageSize);

/// <summary>A homework in the student portal (the student's own assignment of it).</summary>
public sealed record StudentHomeworkResponse(
    Guid Id,
    string Title,
    string? Description,
    DateOnly? DueDate,
    string Status,
    DateTime AssignedAtUtc,
    DateTime? SubmittedAtUtc,
    bool Late,
    string? AnswerText,
    HomeworkFileResponse? File,
    DateTime? ReviewedAtUtc,
    string? TeacherComment,
    int Version);
