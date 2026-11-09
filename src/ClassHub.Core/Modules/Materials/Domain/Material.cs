using ClassHub.Core.Infrastructure.Files;
using ClassHub.Core.SharedKernel.Tenancy;

namespace ClassHub.Core.Modules.Materials.Domain;

/// <summary>
/// A teaching material (R2-P8): a stored file with a title, an optional description and subject. Private by default:
/// only the teacher and the students it is assigned to (directly or through a group) can open it.
/// </summary>
public sealed class Material : ITenantOwned, IArchivable
{
    public const int TitleMaxLength = 120;
    public const int DescriptionMaxLength = 1000;
    public const int FileNameMaxLength = DocumentFiles.FileNameMaxLength;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    public Guid? SubjectId { get; set; }

    /// <summary>The stored file (<c>StoredFiles</c>, purpose <see cref="MaterialFiles.Purpose"/>).</summary>
    public Guid FileId { get; set; }

    /// <summary>The uploaded name, made safe (no path, no control or reserved characters).</summary>
    public required string FileName { get; set; }

    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? ArchivedAtUtc { get; set; }

    public int Version { get; set; }
}

/// <summary>A material given to one student or one group (exactly one of the two).</summary>
public sealed class MaterialAssignment : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid MaterialId { get; set; }

    public Guid? StudentId { get; set; }

    public Guid? GroupId { get; set; }

    public DateTime AssignedAtUtc { get; set; }
}

/// <summary>Materials in <c>StoredFiles</c>; the upload rules are <see cref="DocumentFiles"/>.</summary>
public static class MaterialFiles
{
    public const string Purpose = "material";
}
