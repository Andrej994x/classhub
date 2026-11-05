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
    int Version,
    /// <summary>Why platform moderation hid the test from the public (R1-P11); null when it is not hidden.</summary>
    string? HiddenReason = null,
    DateTime? HiddenAtUtc = null);

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
    DateTime UpdatedAtUtc,
    bool HiddenByModeration = false);

public sealed record TestVersionRequest(int? Version);

/// <summary>A test as moderation sees it (R1-P11).</summary>
public sealed record ModeratedTest(Guid WorkspaceId, Guid TestId, string Slug, string Title, bool Public, string? HiddenReason, DateTime? HiddenAtUtc);

/// <summary>
/// Moderation of diagnostic tests across tenants (allow-listed queries; platform staff with MFA, or the anonymous
/// report endpoint for <see cref="FindPublicAsync"/>). Hide/show change tracked rows; the caller saves and evicts.
/// </summary>
public interface ITestModeration
{
    /// <summary>A test a visitor can see now in the workspace (published, not archived, not hidden), for reports.</summary>
    Task<ModeratedTest?> FindPublicAsync(Guid workspaceId, string testSlug, CancellationToken cancellationToken);

    Task<ModeratedTest?> FindAsync(Guid workspaceId, Guid testId, CancellationToken cancellationToken);

    Task<bool> HideAsync(Guid workspaceId, Guid testId, string reason, DateTime nowUtc, CancellationToken cancellationToken);

    Task<bool> UnhideAsync(Guid workspaceId, Guid testId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ModeratedTest>> ListHiddenAsync(CancellationToken cancellationToken);
}

