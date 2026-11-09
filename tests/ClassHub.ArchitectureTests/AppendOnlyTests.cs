using System.Text.RegularExpressions;

namespace ClassHub.ArchitectureTests;

/// <summary>
/// R2-P10: the finance ledger is append-only. The save interceptor refuses tracked updates and deletes of
/// <c>IAppendOnly</c> rows; bulk statements bypass it, so no code may run one against those tables.
/// </summary>
public sealed partial class AppendOnlyTests
{
    [GeneratedRegex(@"Set<(LessonLedgerEntry|Payment|BalanceAdjustment)>\(\)[^;]*\.(ExecuteDelete|ExecuteUpdate|Remove|RemoveRange|Update|UpdateRange|Attach)(Async)?\s*\(")]
    private static partial Regex ChangePattern();

    [GeneratedRegex(@"\b(UPDATE|DELETE\s+FROM|DELETE)\s+\[?(LessonLedger|Payments|BalanceAdjustments)\]?", RegexOptions.IgnoreCase)]
    private static partial Regex SqlChangePattern();

    [Fact]
    public void No_code_updates_or_deletes_ledger_rows()
    {
        var root = RepositoryRoot();
        var violations = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => (Path: Path.GetRelativePath(root, f).Replace('\\', '/'), Text: File.ReadAllText(f)))
            .Where(f => ChangePattern().IsMatch(f.Text) || SqlChangePattern().IsMatch(f.Text))
            .Select(f => f.Path)
            .ToList();

        violations.ShouldBeEmpty("Ledger rows changed or deleted outside a reviewed exception: " + string.Join(", ", violations));
    }

    [Theory]
    [InlineData("db.Set<LessonLedgerEntry>().Where(e => e.Id == id).ExecuteDeleteAsync(ct)", true)]
    [InlineData("db.Set<Payment>().Remove(payment)", true)]
    [InlineData("db.Set<BalanceAdjustment>().Where(a => a.StudentId == s).ExecuteUpdateAsync(x => x)", true)]
    [InlineData("db.Set<Payment>().Add(payment)", false)]
    [InlineData("db.Set<StoredFile>().Remove(file)", false)]
    public void The_detector_finds_changes_of_ledger_rows(string code, bool expected) => ChangePattern().IsMatch(code).ShouldBe(expected);

    [Theory]
    [InlineData("UPDATE [LessonLedger] SET [Quantity] = 2")]
    [InlineData("DELETE FROM Payments WHERE 1 = 1")]
    public void The_detector_finds_raw_sql_changes_of_ledger_rows(string sql) => SqlChangePattern().IsMatch(sql).ShouldBeTrue();

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
