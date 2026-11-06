using ClassHub.Core.SharedKernel.Tenancy;

namespace ClassHub.Core.Modules.Groups.Domain;

/// <summary>
/// A teaching group (M06, R2-P2): a name, an optional subject and its members. Archiving hides it from the active list
/// and keeps everything (members, and the lesson history that comes with R2-P4).
/// </summary>
public sealed class Group : ITenantOwned, IArchivable
{
    public const int NameMaxLength = 80;
    public const int DescriptionMaxLength = 500;
    public const int MaxMembers = 100;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public required string Name { get; set; }

    public Guid? SubjectId { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? ArchivedAtUtc { get; set; }

    public int Version { get; set; }
}

/// <summary>
/// A student's membership in a group. Removing a member only ends the membership (<see cref="RemovedAtUtc"/>), so
/// lessons that referred to it keep their history; adding the student again reopens the same row.
/// </summary>
public sealed class GroupMember : ITenantOwned
{
    public Guid WorkspaceId { get; set; }

    public Guid GroupId { get; set; }

    public Guid StudentId { get; set; }

    public DateTime AddedAtUtc { get; set; }

    public DateTime? RemovedAtUtc { get; set; }
}
