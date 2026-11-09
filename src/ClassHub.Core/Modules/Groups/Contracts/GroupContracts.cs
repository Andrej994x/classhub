namespace ClassHub.Core.Modules.Groups.Contracts;

/// <summary>The group entity, for database foreign keys from other modules (group lessons in R2-P4).</summary>
public static class GroupForeignKeyTargets
{
    public static readonly Type Group = typeof(Domain.Group);
}

public sealed record GroupMemberResponse(Guid StudentId, string FirstName, string? LastName, bool StudentArchived, DateTime AddedAtUtc);

public sealed record GroupResponse(
    Guid Id,
    string Name,
    Guid? SubjectId,
    string? Description,
    bool Archived,
    IReadOnlyList<GroupMemberResponse> Members,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int Version);

public sealed record GroupListItem(Guid Id, string Name, Guid? SubjectId, int MemberCount, bool Archived, DateTime UpdatedAtUtc);

/// <summary>Create (no version) or update (the version that was loaded) a group's details.</summary>
public sealed record SaveGroupRequest(string? Name, Guid? SubjectId, string? Description, int? Version);

public sealed record GroupVersionRequest(int? Version);

/// <summary>Add a student of this workspace (not archived) to the group.</summary>
public sealed record AddGroupMemberRequest(Guid? StudentId);

/// <summary>A group for other modules (lessons): its name, state and the students currently in it who are not archived.</summary>
public sealed record GroupSummary(Guid Id, string Name, bool Archived, IReadOnlyList<Guid> ActiveStudentIds);

/// <summary>Groups of the current workspace (archived ones included).</summary>
public interface IGroupLookup
{
    Task<IReadOnlyDictionary<Guid, GroupSummary>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>The groups (not archived) the student is currently a member of.</summary>
    Task<IReadOnlyList<Guid>> ActiveGroupsOfStudentAsync(Guid studentId, CancellationToken cancellationToken);
}
