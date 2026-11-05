using System.Security.Cryptography;
using System.Text;
using ClassHub.Core.Modules.Catalog.Domain;

namespace ClassHub.Core.Modules.Catalog.Persistence;

/// <summary>
/// Seed data for the catalog (migration <c>AddCatalogProfilesAndFiles</c>). Ids are derived from the stable
/// codes, so they never change between builds and other data can rely on them.
/// </summary>
internal static class CatalogSeed
{
    /// <summary>Deterministic id for a catalog code (name-based, SHA-256 of "kind:code").</summary>
    public static Guid IdFor(string kind, string code)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"classhub-catalog:{kind}:{code}"));
        var bytes = hash.AsSpan(0, 16).ToArray();
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x80); // version nibble (custom, name-based)
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(bytes);
    }

    public static readonly (string Code, string Mk, string En)[] Categories =
    [
        ("languages", "Јазици", "Languages"),
        ("math-science", "Математика и природни науки", "Mathematics & science"),
        ("social", "Општествени науки", "Humanities & social sciences"),
        ("it", "Информатика", "Computing"),
        ("arts", "Уметност и музика", "Arts & music"),
        ("exams", "Подготовка за испити", "Exam preparation"),
    ];

    public static readonly (string Code, string Category, string Mk, string En)[] Subjects =
    [
        ("english", "languages", "Англиски јазик", "English"),
        ("german", "languages", "Германски јазик", "German"),
        ("french", "languages", "Француски јазик", "French"),
        ("italian", "languages", "Италијански јазик", "Italian"),
        ("spanish", "languages", "Шпански јазик", "Spanish"),
        ("russian", "languages", "Руски јазик", "Russian"),
        ("macedonian", "languages", "Македонски јазик", "Macedonian"),
        ("albanian", "languages", "Албански јазик", "Albanian"),
        ("turkish", "languages", "Турски јазик", "Turkish"),
        ("math", "math-science", "Математика", "Mathematics"),
        ("physics", "math-science", "Физика", "Physics"),
        ("chemistry", "math-science", "Хемија", "Chemistry"),
        ("biology", "math-science", "Биологија", "Biology"),
        ("geography", "social", "Географија", "Geography"),
        ("history", "social", "Историја", "History"),
        ("philosophy", "social", "Филозофија", "Philosophy"),
        ("economics", "social", "Економија", "Economics"),
        ("programming", "it", "Програмирање", "Programming"),
        ("computer-basics", "it", "Основи на информатиката", "Computer basics"),
        ("music", "arts", "Музика", "Music"),
        ("piano", "arts", "Пијано", "Piano"),
        ("guitar", "arts", "Гитара", "Guitar"),
        ("art", "arts", "Ликовна уметност", "Visual arts"),
        ("state-matura", "exams", "Државна матура", "State matura"),
        ("language-certificates", "exams", "Јазични сертификати", "Language certificates"),
    ];

    public static readonly (string Code, string Mk, string En)[] Levels =
    [
        ("preschool", "Предучилишно", "Preschool"),
        ("primary-lower", "Основно (1–5 одд.)", "Primary (grades 1–5)"),
        ("primary-upper", "Основно (6–9 одд.)", "Primary (grades 6–9)"),
        ("secondary", "Средно", "Secondary"),
        ("university", "Факултет", "University"),
        ("adults", "Возрасни", "Adults"),
    ];

    public static IEnumerable<SubjectCategory> CategoryRows() =>
        Categories.Select((c, i) => new SubjectCategory { Id = IdFor("category", c.Code), Code = c.Code, NameMk = c.Mk, NameEn = c.En, SortOrder = (i + 1) * 10 });

    public static IEnumerable<Subject> SubjectRows() =>
        Subjects.Select((s, i) => new Subject
        {
            Id = IdFor("subject", s.Code),
            Code = s.Code,
            CategoryId = IdFor("category", s.Category),
            NameMk = s.Mk,
            NameEn = s.En,
            SortOrder = (i + 1) * 10,
            IsActive = true,
        });

    public static IEnumerable<EducationLevel> LevelRows() =>
        Levels.Select((l, i) => new EducationLevel { Id = IdFor("level", l.Code), Code = l.Code, NameMk = l.Mk, NameEn = l.En, SortOrder = (i + 1) * 10 });
}
