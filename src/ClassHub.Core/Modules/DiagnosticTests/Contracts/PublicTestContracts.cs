namespace ClassHub.Core.Modules.DiagnosticTests.Contracts;

/// <summary>
/// A published test as visitors receive it: never the correct options (architecture test). <c>SelectSeveral</c> tells
/// the island to offer checkboxes instead of radio buttons.
/// </summary>
public sealed record PublicTestResponse(
    /// <summary>Send back with the attempt: answers to an older version are refused (409 TEST_CHANGED).</summary>
    int Version,
    string Title,
    string Slug,
    string? Description,
    string Difficulty,
    string? Subject,
    int TotalPoints,
    IReadOnlyList<PublicTestQuestion> Questions);

public sealed record PublicTestQuestion(Guid Id, string Text, int Points, bool SelectSeveral, IReadOnlyList<PublicTestOption> Options);

public sealed record PublicTestOption(Guid Id, string Text);

/// <summary>The visitor's choices for the test version they were shown: for each answered question the chosen option ids.</summary>
public sealed record SubmitAttemptRequest(int? Version, IReadOnlyList<AttemptAnswer>? Answers);

public sealed record AttemptAnswer(Guid QuestionId, IReadOnlyList<Guid>? OptionIds);

/// <summary>The score only (no per-question correctness, so the answers cannot be read back from results).</summary>
public sealed record PublicAttemptResult(Guid AttemptId, int Points, int MaxPoints, int QuestionsRight, int TotalQuestions);

/// <summary>A published test listed on the teacher's public site (and in the sitemap).</summary>
public sealed record PublishedTestLink(string Slug, string Title, DateTime PublishedAtUtc);

/// <summary>The teacher's view of a test's attempts: the count, and results only where the visitor shared them (D-18).</summary>
public sealed record TestAttemptsResponse(int Total, int SharedCount, IReadOnlyList<SharedAttemptResult> Shared);

public sealed record SharedAttemptResult(Guid AttemptId, int Points, int MaxPoints, int CorrectQuestions, int TotalQuestions, DateTime CompletedAtUtc, Guid? LeadId);

/// <summary>A diagnostic result as the lead detail shows it: the score only when the visitor shared it (D-18).</summary>
public sealed record DiagnosticResultSummary(Guid TestId, string TestTitle, int? Points, int? MaxPoints);

/// <summary>Database foreign key target for leads that belong to an attempt.</summary>
public static class DiagnosticTestForeignKeyTargets
{
    public static readonly Type Attempt = typeof(Domain.TestAttempt);
}

/// <summary>
/// Public tests for the site renderer, the sitemap and the Razor test page. It has no dependency on the Sites module
/// (the site renderer uses it), so callers resolve and bind the site themselves.
/// </summary>
public interface IPublicTests
{
    /// <summary>The published test with this address in the current (public read-only) workspace, or null.</summary>
    Task<PublicTestResponse?> FindAsync(string testSlug, string language, CancellationToken cancellationToken);

    /// <summary>Published, non-archived tests of the current (public read-only) workspace, newest first.</summary>
    Task<IReadOnlyList<PublishedTestLink>> ListPublishedAsync(CancellationToken cancellationToken);

    /// <summary>Published tests of the given workspaces (sitemap; no tenant binding needed).</summary>
    Task<IReadOnlyList<(Guid WorkspaceId, PublishedTestLink Test)>> ListPublishedForWorkspacesAsync(IReadOnlyCollection<Guid> workspaceIds, CancellationToken cancellationToken);
}

/// <summary>Used by the lead intake (Leads module) to attach a contact request to an attempt.</summary>
public interface IDiagnosticAttemptLink
{
    /// <summary>True when the attempt exists in the current (public read-only) workspace and has no lead yet.</summary>
    Task<bool> CanLinkAsync(Guid attemptId, CancellationToken cancellationToken);

    /// <summary>
    /// Records the lead on the attempt (and the visitor's consent to share the result) inside the caller's unit of work,
    /// which writes with the workspace set explicitly.
    /// </summary>
    Task LinkAsync(Guid workspaceId, Guid attemptId, Guid leadId, bool shareResult, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>The result for the teacher's lead detail (current workspace), or null.</summary>
    Task<DiagnosticResultSummary?> GetResultAsync(Guid attemptId, CancellationToken cancellationToken);
}

/// <summary>Test numbers of the current workspace for the dashboard.</summary>
public sealed record TestStats(int Published, int AttemptsInPeriod, int SharedResultsInPeriod);

public interface ITestStats
{
    Task<TestStats> GetAsync(DateTime fromUtc, CancellationToken cancellationToken);
}
