using ClassHub.Core.SharedKernel.Tenancy;

namespace ClassHub.Core.Modules.Students.Domain;

/// <summary>
/// A student in the teacher's records (M06, R2-P1). Exists without a login (accounts come with invitations, R2-P3);
/// archiving hides the student from the active list and keeps the history. For minors the guardian's contact is
/// recorded (D-18).
/// </summary>
public sealed class Student : ITenantOwned, IArchivable
{
    public const int NameMaxLength = 60;
    public const int EmailMaxLength = 254;
    public const int PhoneMaxLength = 40;
    public const int GuardianNameMaxLength = 120;
    public const int MaxSubjects = 20;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public required string FirstName { get; set; }

    public string? LastName { get; set; }

    /// <summary>Lowercase; unique within the workspace (the dedupe key of lead conversions).</summary>
    public string? Email { get; set; }

    public string? Phone { get; set; }

    public bool IsMinor { get; set; }

    public string? GuardianName { get; set; }

    public string? GuardianEmail { get; set; }

    public string? GuardianPhone { get; set; }

    /// <summary>The request the student was created from, when converted from a lead.</summary>
    public Guid? SourceLeadId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? ArchivedAtUtc { get; set; }

    public int Version { get; set; }
}

public sealed class StudentSubject : ITenantOwned
{
    public Guid WorkspaceId { get; set; }

    public Guid StudentId { get; set; }

    public Guid SubjectId { get; set; }
}

/// <summary>The teacher's private note about a student: never shown to the student (or anyone else).</summary>
public sealed class StudentNote : ITenantOwned
{
    public const int TextMaxLength = 2000;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid StudentId { get; set; }

    public required string Text { get; set; }

    public Guid? AuthorUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Pure helpers for names and emails (unit-tested).</summary>
public static class StudentNames
{
    /// <summary>"Ana Maria Petrovska" → ("Ana", "Maria Petrovska"); a single word has no last name.</summary>
    public static (string First, string? Last) Split(string fullName)
    {
        var parts = (fullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return (string.Empty, null);
        }

        var first = Truncate(parts[0], Student.NameMaxLength);
        var last = parts.Length > 1 ? Truncate(string.Join(' ', parts[1..]), Student.NameMaxLength) : null;
        return (first, last);
    }

    /// <summary>The dedupe form of an email: trimmed and lowercase; empty becomes null.</summary>
    public static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
