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
    int Version);

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
