using System.Text.RegularExpressions;

namespace ClassHub.ArchitectureTests;

/// <summary>
/// Layer 6 of tenant isolation (docs/ROADMAP.md C.7): code that can bypass the tenant query filter or run
/// raw SQL is only allowed in reviewed places. Adding a new one means adding it here, in a reviewed PR.
/// </summary>
public sealed partial class TenantBypassTests
{
    private static readonly string[] AllowedPathPatterns =
    [
        @"^src/ClassHub\.Core/Infrastructure/Persistence/Migrations/",           // generated migrations
        @"^src/ClassHub\.Core/Infrastructure/Email/EmailOutboxDispatcher\.cs$",  // lease-based claim (no tenant data)
        @"^src/ClassHub\.Core/Modules/[^/]+/Persistence/[^/]*Queries\.cs$",      // explicit public/admin/access queries
        @"^src/ClassHub\.Core/Modules/[^/]+/Jobs/",                                // background jobs (system scope)
    ];

    [GeneratedRegex(@"\b(IgnoreQueryFilters|ExecuteSqlRaw|ExecuteSqlInterpolated|ExecuteSql|SqlQueryRaw|SqlQuery|FromSqlRaw|FromSqlInterpolated|FromSql)\s*[<(]")]
    private static partial Regex BypassPattern();

    [Fact]
    public void Tenant_filter_bypasses_and_raw_sql_only_appear_in_allow_listed_files()
    {
        var root = RepositoryRoot();
        var violations = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => (Path: Path.GetRelativePath(root, f).Replace('\\', '/'), Text: File.ReadAllText(f)))
            .Where(f => BypassPattern().IsMatch(f.Text))
            .Where(f => !AllowedPathPatterns.Any(p => Regex.IsMatch(f.Path, p)))
            .Select(f => f.Path)
            .ToList();

        violations.ShouldBeEmpty("Tenant filter bypass or raw SQL outside the allow list: " + string.Join(", ", violations));
    }

    [Fact]
    public void The_allow_list_is_actually_used()
    {
        var root = RepositoryRoot();
        var files = Directory.EnumerateFiles(Path.Combine(root, "src"), "*Queries.cs", SearchOption.AllDirectories).ToList();
        files.ShouldContain(f => File.ReadAllText(f).Contains("IgnoreQueryFilters", StringComparison.Ordinal));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ClassHub.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
