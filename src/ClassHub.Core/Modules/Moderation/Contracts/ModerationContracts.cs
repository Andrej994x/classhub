namespace ClassHub.Core.Modules.Moderation.Contracts;

/// <summary>
/// A visitor's report (public, anonymous). <c>Target</c>: <c>site</c>, <c>section</c> (with <c>SectionType</c>) or
/// <c>test</c> (with <c>TestSlug</c>). <c>Website</c> is a honeypot: people leave it empty.
/// </summary>
public sealed record SubmitReportRequest(string? Target, string? SectionType, string? TestSlug, string? Reason, string? Details, string? Email, string? Website);

/// <summary>A report as platform staff see it, with the reported content and whether it is hidden now.</summary>
public sealed record AdminReportResponse(
    Guid Id,
    DateTime CreatedAtUtc,
    string Status,
    string Target,
    string Reason,
    string? Details,
    string? ReporterEmail,
    Guid WorkspaceId,
    string? SiteSlug,
    string? SectionType,
    Guid? TestId,
    string? TestTitle,
    bool TargetHidden,
    int OpenReportsForTarget,
    DateTime? ResolvedAtUtc,
    Guid? ResolvedByUserId,
    string? ResolutionNote);

public sealed record AdminReportPage(IReadOnlyList<AdminReportResponse> Items, int Total, int Page, int PageSize);

/// <summary>Dismiss a report, with an optional note for staff.</summary>
public sealed record ModerationDecisionRequest(string? Reason);

/// <summary>Hide the reported content; the reason is required and shown to the teacher.</summary>
public sealed record HideContentRequest(string? Reason);

/// <summary>Content hidden by moderation now. <c>Target</c>: site, section or test.</summary>
public sealed record HiddenContentResponse(Guid WorkspaceId, string Target, string? SiteSlug, string? SectionType, Guid? TestId, string? TestTitle, string Reason, DateTime HiddenAtUtc);

/// <summary>Show hidden content again; the reason is recorded in the audit log and sent to the teacher.</summary>
public sealed record RestoreContentRequest(Guid? WorkspaceId, string? Target, string? SectionType, Guid? TestId, string? Reason);
