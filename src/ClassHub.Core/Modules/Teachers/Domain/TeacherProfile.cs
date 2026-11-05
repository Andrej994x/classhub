using ClassHub.Core.SharedKernel.Tenancy;

namespace ClassHub.Core.Modules.Teachers.Domain;

public enum TeachingMode
{
    Online = 1,
    InPerson = 2,
    Hybrid = 3,
}

/// <summary>The teacher's professional profile: one per workspace (M01 "Наставнички профил").</summary>
public sealed class TeacherProfile : ITenantOwned
{
    public const int NameMaxLength = 100;
    public const int BioMaxLength = 1000;
    public const int MaxSubjects = 20;
    public const int MaxLevels = 10;
    public const int MaxLanguages = 10;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Bio { get; set; }

    public TeachingMode? TeachingMode { get; set; }

    /// <summary>ISO 3166-1 alpha-2.</summary>
    public string? CountryCode { get; set; }

    /// <summary>The current profile photo (a <c>StoredFile</c> of the same workspace).</summary>
    public Guid? PhotoFileId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = [];
}

public sealed class TeacherProfileSubject : ITenantOwned
{
    public Guid WorkspaceId { get; set; }

    public Guid ProfileId { get; set; }

    public Guid SubjectId { get; set; }
}

public sealed class TeacherProfileLevel : ITenantOwned
{
    public Guid WorkspaceId { get; set; }

    public Guid ProfileId { get; set; }

    public Guid LevelId { get; set; }
}

public sealed class TeacherProfileLanguage : ITenantOwned
{
    public const int CodeMaxLength = 10;

    public Guid WorkspaceId { get; set; }

    public Guid ProfileId { get; set; }

    /// <summary>ISO 639-1 (see <c>TeachingLanguages</c>).</summary>
    public required string LanguageCode { get; set; }
}
