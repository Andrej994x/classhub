namespace ClassHub.Core.Modules.DiagnosticTests.Contracts;

/// <summary>
/// The whole test as the editor saves it. Questions and options keep their ids (null = new), so attempts can refer to
/// them. <c>Slug</c> may be empty when creating (derived from the title). <c>Difficulty</c>: Beginner, Intermediate or
/// Advanced. <c>Version</c>: null when creating, then the version the client loaded (409 if stale).
/// </summary>
public sealed record SaveDiagnosticTestRequest(
    string? Title,
    string? Slug,
    Guid? SubjectId,
    string? Description,
    string? Difficulty,
    IReadOnlyList<SaveTestQuestion>? Questions,
    int? Version = null);

public sealed record SaveTestQuestion(Guid? Id, string? Text, int Points, IReadOnlyList<SaveTestOption>? Options);

public sealed record SaveTestOption(Guid? Id, string? Text, bool IsCorrect);

/// <summary>The teacher's full view, including the correct options (never used for anything public).</summary>
public sealed record DiagnosticTestResponse(
    Guid Id,
    string Title,
    string Slug,
    Guid SubjectId,
    string? Description,
    string Difficulty,
    string Status,
    DateTime? PublishedAtUtc,
    int AttemptCount,
    bool Archived,
    IReadOnlyList<TestQuestionResponse> Questions,
    int TotalPoints,
    DateTime UpdatedAtUtc,
    int Version);

public sealed record TestQuestionResponse(Guid Id, string Text, int Points, IReadOnlyList<TestOptionResponse> Options);

public sealed record TestOptionResponse(Guid Id, string Text, bool IsCorrect);

public sealed record DiagnosticTestListItem(
    Guid Id,
    string Title,
    string Slug,
    Guid SubjectId,
    string Difficulty,
    string Status,
    int QuestionCount,
    int TotalPoints,
    int AttemptCount,
    DateTime UpdatedAtUtc);

public sealed record TestVersionRequest(int? Version);

/// <summary>
/// Plan limit hook for published tests (Free/Pro, D-29). Until R1-P10 enforces the plans, every workspace may publish
/// any number of tests.
/// </summary>
public interface IPublishedTestQuota
{
    /// <summary>Throws when publishing one more test would exceed the workspace's plan.</summary>
    Task EnsureCanPublishAsync(CancellationToken cancellationToken);
}
