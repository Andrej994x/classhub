namespace ClassHub.Core.Modules.Leads.Contracts;

/// <summary>
/// What the public contact form needs (fetched when the island mounts, never cached): whether the teacher can receive
/// leads (D-10b), a signed form token (minimum fill time), the requestable offers and subjects, and the versioned
/// consent and minors notice in the site language (D-18).
/// </summary>
public sealed record LeadFormResponse(
    bool Available,
    string? FormToken,
    IReadOnlyList<LeadFormOption> Offers,
    IReadOnlyList<LeadFormOption> Subjects,
    string ConsentVersion,
    string ConsentText,
    string MinorNotice,
    int MinorAgeThreshold,
    /// <summary>The form must not be sent sooner after loading (the island waits out the rest itself).</summary>
    int MinimumFillSeconds);

public sealed record LeadFormOption(Guid Id, string Name);

/// <summary>
/// A visitor's request. <c>Type</c>: <c>Message</c>, <c>LessonRequest</c> or <c>Diagnostic</c> (after a test, with the
/// <c>AttemptId</c> the attempt returned; <c>ShareResult</c> lets the teacher see the score, D-18). <c>Website</c> is a
/// honeypot that people never fill in. <c>Consent</c> and <c>AgeConfirmed</c> must be true and <c>ConsentVersion</c> current.
/// </summary>
public sealed record SubmitLeadRequest(
    string? Type,
    string? Name,
    string? Email,
    string? Phone,
    string? Message,
    Guid? OfferId,
    Guid? SubjectId,
    bool Consent,
    bool AgeConfirmed,
    string? ConsentVersion,
    string? FormToken,
    string? Website,
    Guid? AttemptId = null,
    bool ShareResult = false);

/// <summary>Always the same answer: nothing the visitor sent is echoed back.</summary>
public sealed record LeadReceivedResponse(bool Received);

public sealed record LeadListItem(
    Guid Id,
    string Type,
    string Status,
    string Name,
    string Email,
    string? OfferTitle,
    Guid? SubjectId,
    string? Preview,
    DateTime CreatedAtUtc,
    bool Archived);

public sealed record LeadListResponse(IReadOnlyList<LeadListItem> Items, int Total, int NewCount, int Page, int PageSize);

public sealed record LeadNoteResponse(Guid Id, string Text, DateTime CreatedAtUtc);

public sealed record LeadResponse(
    Guid Id,
    string Type,
    string Status,
    string Name,
    string Email,
    string? Phone,
    string? Message,
    Guid? OfferId,
    string? OfferTitle,
    Guid? SubjectId,
    string ConsentVersion,
    DateTime ConsentedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    bool Archived,
    IReadOnlyList<LeadNoteResponse> Notes,
    int Version,
    /// <summary>For diagnostic requests: the test and, when the visitor shared it, the score.</summary>
    ClassHub.Core.Modules.DiagnosticTests.Contracts.DiagnosticResultSummary? DiagnosticResult);

/// <summary><c>Status</c>: New, Contacted, Converted or Closed. <c>Version</c>: the version the client loaded.</summary>
public sealed record ChangeLeadStatusRequest(string? Status, int? Version);

public sealed record AddLeadNoteRequest(string? Text);

public sealed record LeadVersionRequest(int? Version);

/// <summary>Leads other modules may reference with (composite, same-workspace) database foreign keys.</summary>
public static class LeadForeignKeyTargets
{
    public static readonly Type Lead = typeof(Domain.Lead);
}

/// <summary>
/// Lead numbers of the current workspace for the dashboard. <c>ByStatus</c>: current, not archived. Period numbers count
/// every lead that arrived since <c>fromUtc</c> (archived ones too), with its current status.
/// </summary>
public sealed record LeadStats(
    int New,
    IReadOnlyDictionary<string, int> ByStatus,
    int InPeriod,
    IReadOnlyDictionary<string, int> ByTypeInPeriod,
    int ContactedInPeriod,
    int ConvertedInPeriod);

public interface ILeadStats
{
    Task<LeadStats> GetAsync(DateTime fromUtc, CancellationToken cancellationToken);
}
