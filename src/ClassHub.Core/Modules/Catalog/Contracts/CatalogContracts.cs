namespace ClassHub.Core.Modules.Catalog.Contracts;

/// <summary>
/// Catalog entities other modules may reference with database foreign keys (as a Type, so they do not
/// depend on the catalog's domain types).
/// </summary>
public static class CatalogForeignKeyTargets
{
    public static readonly Type Subject = typeof(Domain.Subject);
    public static readonly Type EducationLevel = typeof(Domain.EducationLevel);

    /// <summary>The fixed id of a seeded subject (for other modules' seed data, e.g. the toolbox).</summary>
    public static Guid SeededSubjectId(string code) => Persistence.CatalogSeed.IdFor("subject", code);
}

/// <summary>A catalog entry with both UI languages (the SPA shows the one matching the UI language).</summary>
public sealed record CatalogItem(Guid Id, string Code, string NameMk, string NameEn);

public sealed record CatalogSubject(Guid Id, string Code, Guid CategoryId, string NameMk, string NameEn);

public sealed record CatalogLanguage(string Code, string NameMk, string NameEn);

public sealed record CatalogResponse(
    IReadOnlyList<CatalogItem> Categories,
    IReadOnlyList<CatalogSubject> Subjects,
    IReadOnlyList<CatalogItem> Levels,
    IReadOnlyList<CatalogLanguage> Languages,
    /// <summary>ISO 3166-1 alpha-2 codes; the SPA shows localized names (Intl.DisplayNames).</summary>
    IReadOnlyList<string> Countries);

/// <summary>A catalog entry for platform staff, active or not (R1-P11). <c>CategoryId</c> only for subjects.</summary>
public sealed record AdminCatalogEntry(Guid Id, string Code, string NameMk, string NameEn, int SortOrder, bool IsActive, Guid? CategoryId);

public sealed record AdminCatalogResponse(IReadOnlyList<AdminCatalogEntry> Categories, IReadOnlyList<AdminCatalogEntry> Subjects, IReadOnlyList<AdminCatalogEntry> Levels);

/// <summary>
/// Create (with a <c>Code</c>, which never changes afterwards) or update a category, subject (with <c>CategoryId</c>) or
/// level. Entries are deactivated, never deleted: existing references stay, new choices hide them.
/// </summary>
public sealed record CatalogEntryRequest(string? Code, string? NameMk, string? NameEn, int? SortOrder, bool? IsActive, Guid? CategoryId);

/// <summary>Used by other modules (through Contracts) to validate catalog references.</summary>
public interface ICatalogLookup
{
    /// <summary>The ids among <paramref name="ids"/> that are not active subjects.</summary>
    Task<IReadOnlyList<Guid>> UnknownSubjectsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>The ids among <paramref name="ids"/> that are not education levels.</summary>
    Task<IReadOnlyList<Guid>> UnknownLevelsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>The given subjects (active ones only), in catalog order (public site).</summary>
    Task<IReadOnlyList<CatalogItem>> SubjectsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>The given education levels, in catalog order (public site).</summary>
    Task<IReadOnlyList<CatalogItem>> LevelsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}

/// <summary>
/// Languages a teacher can teach in (ISO 639-1). A fixed list for the region and the most common teaching
/// languages; extended through reference data admin later (R1-P11).
/// </summary>
public static class TeachingLanguages
{
    public static IReadOnlyList<CatalogLanguage> All { get; } =
    [
        new("mk", "Македонски", "Macedonian"),
        new("en", "Англиски", "English"),
        new("sq", "Албански", "Albanian"),
        new("sr", "Српски", "Serbian"),
        new("bg", "Бугарски", "Bulgarian"),
        new("hr", "Хрватски", "Croatian"),
        new("tr", "Турски", "Turkish"),
        new("de", "Германски", "German"),
        new("fr", "Француски", "French"),
        new("it", "Италијански", "Italian"),
        new("es", "Шпански", "Spanish"),
        new("ru", "Руски", "Russian"),
        new("el", "Грчки", "Greek"),
    ];

    private static readonly HashSet<string> Codes = new(All.Select(l => l.Code), StringComparer.Ordinal);

    public static bool IsKnown(string? code) => code is not null && Codes.Contains(code);
}
