namespace ClassHub.Core.Modules.Moderation.Domain;

/// <summary>What a visitor reported: the whole site, one section of it, or one public test.</summary>
public enum ReportTarget
{
    Site = 1,
    Section = 2,
    Test = 3,
}

public enum ReportReason
{
    Spam = 1,
    Inappropriate = 2,
    Misleading = 3,
    Copyright = 4,
    PersonalData = 5,
    Other = 6,
}

public enum ReportStatus
{
    Open = 1,

    /// <summary>The content was hidden (this report or another one about the same content).</summary>
    Resolved = 2,

    /// <summary>No action needed.</summary>
    Dismissed = 3,
}

/// <summary>
/// A visitor's report about public content (M15 moderation, R1-P11). Platform data, not tenant-owned: only platform
/// staff read it, and the reporter's details (text, optional email) are never shown to the teacher.
/// </summary>
public sealed class AbuseReport
{
    public const int DetailsMaxLength = 1000;
    public const int EmailMaxLength = 254;
    public const int SectionTypeMaxLength = 30;
    public const int NoteMaxLength = 500;

    public Guid Id { get; set; }

    /// <summary>The workspace whose content was reported.</summary>
    public Guid WorkspaceId { get; set; }

    public ReportTarget Target { get; set; }

    public string? SectionType { get; set; }

    public Guid? TestId { get; set; }

    public ReportReason Reason { get; set; }

    public string? Details { get; set; }

    /// <summary>Optional, for follow-up questions by platform staff only.</summary>
    public string? ReporterEmail { get; set; }

    public ReportStatus Status { get; set; } = ReportStatus.Open;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ResolvedAtUtc { get; set; }

    public Guid? ResolvedByUserId { get; set; }

    public string? ResolutionNote { get; set; }

    /// <summary>Concurrency token: of two decisions about the same report, the second one fails (409).</summary>
    public int Version { get; set; }
}

/// <summary>Wire names of the report reasons and targets (kebab-case).</summary>
public static class ReportNames
{
    public static IReadOnlyDictionary<string, ReportReason> Reasons { get; } = new Dictionary<string, ReportReason>(StringComparer.Ordinal)
    {
        ["spam"] = ReportReason.Spam,
        ["inappropriate"] = ReportReason.Inappropriate,
        ["misleading"] = ReportReason.Misleading,
        ["copyright"] = ReportReason.Copyright,
        ["personal-data"] = ReportReason.PersonalData,
        ["other"] = ReportReason.Other,
    };

    public static IReadOnlyDictionary<string, ReportTarget> Targets { get; } = new Dictionary<string, ReportTarget>(StringComparer.Ordinal)
    {
        ["site"] = ReportTarget.Site,
        ["section"] = ReportTarget.Section,
        ["test"] = ReportTarget.Test,
    };

    public static string Of(ReportReason reason) => Reasons.Single(r => r.Value == reason).Key;

    public static string Of(ReportTarget target) => Targets.Single(t => t.Value == target).Key;
}
