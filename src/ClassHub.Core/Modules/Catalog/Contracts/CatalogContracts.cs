namespace ClassHub.Core.Modules.Catalog.Contracts;

/// <summary>
/// Catalog entities other modules may reference with database foreign keys (as a Type, so they do not
/// depend on the catalog's domain types).
/// </summary>
public static class CatalogForeignKeyTargets
{
    public static readonly Type Subject = typeof(Domain.Subject);
    public static readonly Type EducationLevel = typeof(Domain.EducationLevel);
}

/// <summary>A catalog entry with both UI languages (the SPA shows the one matching the UI language).</summary>
public sealed record CatalogItem(Guid Id, string Code, string NameMk, string NameEn);

public sealed record CatalogSubject(Guid Id, string Code, Guid CategoryId, string NameMk, string NameEn);

public sealed record CatalogLanguage(string Code, string NameMk, string NameEn);

public sealed record CatalogResponse(
    IReadOnlyList<CatalogItem> Categories,
    IReadOnlyList<CatalogSubject> Subjects,
    IReadOnlyList<CatalogItem> Levels,
    IReadOnlyList<CatalogLanguage> Languages);

/// <summary>Used by other modules (through Contracts) to validate catalog references.</summary>
public interface ICatalogLookup
{
    /// <summary>The ids among <paramref name="ids"/> that are not active subjects.</summary>
    Task<IReadOnlyList<Guid>> UnknownSubjectsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>The ids among <paramref name="ids"/> that are not education levels.</summary>
    Task<IReadOnlyList<Guid>> UnknownLevelsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
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
