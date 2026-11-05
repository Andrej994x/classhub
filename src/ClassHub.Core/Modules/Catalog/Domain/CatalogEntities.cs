namespace ClassHub.Core.Modules.Catalog.Domain;

/// <summary>Platform-wide reference data (not tenant-owned). Seeded in the migration; admin editing in R1-P11.</summary>
public sealed class SubjectCategory
{
    public Guid Id { get; set; }

    public required string Code { get; set; }

    public required string NameMk { get; set; }

    public required string NameEn { get; set; }

    public int SortOrder { get; set; }
}

public sealed class Subject
{
    public Guid Id { get; set; }

    public required string Code { get; set; }

    public Guid CategoryId { get; set; }

    public required string NameMk { get; set; }

    public required string NameEn { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Inactive subjects stay valid on existing profiles but are not offered for new choices.</summary>
    public bool IsActive { get; set; } = true;
}

public sealed class EducationLevel
{
    public Guid Id { get; set; }

    public required string Code { get; set; }

    public required string NameMk { get; set; }

    public required string NameEn { get; set; }

    public int SortOrder { get; set; }
}
