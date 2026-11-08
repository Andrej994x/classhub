namespace ClassHub.Core.Modules.Students.Contracts;

/// <summary>The student entity, for database foreign keys from other modules (lessons, packages, converted leads).</summary>
public static class StudentForeignKeyTargets
{
    public static readonly Type Student = typeof(Domain.Student);
}

/// <summary>A student's name and state, for other modules (group members; lessons later). Never notes or contacts.</summary>
public sealed record StudentSummary(Guid Id, string FirstName, string? LastName, bool Archived);

/// <summary>Students of the current workspace (archived ones included), for other modules.</summary>
public interface IStudentLookup
{
    Task<IReadOnlyDictionary<Guid, StudentSummary>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}

/// <summary>
/// A student for the teacher. Private notes are not part of it (they have their own endpoint and are never part of any
/// student-facing response).
/// </summary>
public sealed record StudentResponse(
    Guid Id,
    string FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    bool IsMinor,
    string? GuardianName,
    string? GuardianEmail,
    string? GuardianPhone,
    IReadOnlyList<Guid> SubjectIds,
    Guid? SourceLeadId,
    bool Archived,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int Version,
    bool AccountLinked,
    StudentInvitationInfo? Invitation);

/// <summary>The pending invitation of a student (the teacher never sees the link or the account's credentials).</summary>
public sealed record StudentInvitationInfo(string Email, DateTime SentAtUtc, DateTime ExpiresAtUtc);

/// <summary>Invite the student to their own account; the email defaults to the student's email.</summary>
public sealed record InviteStudentRequest(string? Email);

/// <summary>The token from an invitation link (<c>/student/invitation#token=…</c>).</summary>
public sealed record InvitationTokenRequest(string? Token);

/// <summary>What the invitation page shows before accepting.</summary>
public sealed record InvitationPreviewResponse(string TeacherName, string StudentFirstName, string Email, bool IsMinor, DateTime ExpiresAtUtc);

/// <summary>
/// Accept with the signed-in account. For a minor, <c>GuardianConsent</c> confirms that a parent or guardian agreed
/// (D-18, provisional wording).
/// </summary>
public sealed record AcceptInvitationRequest(string? Token, bool GuardianConsent);

/// <summary>Accept by creating an account for the invited email (signed in afterwards).</summary>
public sealed record AcceptInvitationWithAccountRequest(string? Token, string? Password, string? Locale, bool GuardianConsent);

/// <summary>A teacher the signed-in user is a student of (D-16: the portal groups data per teacher).</summary>
public sealed record StudentTeacherResponse(Guid WorkspaceId, string TeacherName, Guid StudentId, string FirstName, string? LastName);

/// <summary>The student's own record with one teacher (student portal). Never notes or the teacher's other students.</summary>
public sealed record StudentSelfResponse(Guid WorkspaceId, string TeacherName, Guid StudentId, string FirstName, string? LastName, IReadOnlyList<Guid> SubjectIds);

public sealed record StudentListItem(
    Guid Id,
    string FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    bool IsMinor,
    IReadOnlyList<Guid> SubjectIds,
    bool Archived,
    DateTime UpdatedAtUtc);

public sealed record StudentListResponse(IReadOnlyList<StudentListItem> Items, int Total, int Page, int PageSize, int ActiveCount);

/// <summary>Create (no version) or update (the version that was loaded) a student.</summary>
public sealed record SaveStudentRequest(
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    bool IsMinor,
    string? GuardianName,
    string? GuardianEmail,
    string? GuardianPhone,
    IReadOnlyList<Guid>? SubjectIds,
    int? Version);

public sealed record StudentVersionRequest(int? Version);

public sealed record StudentNoteResponse(Guid Id, string Text, DateTime CreatedAtUtc);

public sealed record AddStudentNoteRequest(string? Text);

/// <summary>Turn a request into a student: linked to the existing student with the same email, or created.</summary>
public sealed record ConvertLeadRequest(Guid? LeadId);

/// <summary><c>Created</c>: a new student; false when the request was linked to an existing one (or already converted).</summary>
public sealed record ConvertLeadResponse(StudentResponse Student, bool Created);

/// <summary>For other modules (booking): the student of a request, converting it if needed (D-12, idempotent).</summary>
public interface ILeadStudents
{
    Task<Guid> EnsureStudentAsync(Guid leadId, CancellationToken cancellationToken);
}
