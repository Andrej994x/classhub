using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.DiagnosticTests.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.DiagnosticTests.Persistence;

/// <summary>
/// Writes of the public test taking, which run in their own scope without a tenant binding and therefore filter by
/// the workspace explicitly (allow-listed tenant-filter bypass), plus the sitemap's cross-workspace list of published
/// tests (only of workspaces the caller already checked as visible).
/// </summary>
internal sealed class TestAttemptQueries(ClassHubDbContext db)
{
    /// <summary>Serializes attempts per test for the caller's transaction (sp_getapplock), so the hourly cap is atomic.</summary>
    public async Task LockAttemptsAsync(Guid testId, CancellationToken cancellationToken)
    {
        var resource = $"classhub:test-attempts:{testId:N}";
        await db.Database.ExecuteSqlAsync(
            $"DECLARE @result int; EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; IF @result < 0 THROW 50003, 'Could not acquire the test attempt lock.', 1;",
            cancellationToken);
    }

    public Task<int> CountAttemptsSinceAsync(Guid workspaceId, Guid testId, DateTime sinceUtc, CancellationToken cancellationToken) =>
        db.Set<TestAttempt>().IgnoreQueryFilters().AsNoTracking()
            .CountAsync(a => a.WorkspaceId == workspaceId && a.TestId == testId && a.CompletedAtUtc >= sinceUtc, cancellationToken);

    /// <summary>Counts the attempt on the test (D-38 lock); the test's own version is not touched.</summary>
    public Task IncrementAttemptCountAsync(Guid workspaceId, Guid testId, CancellationToken cancellationToken) =>
        db.Set<DiagnosticTest>().IgnoreQueryFilters()
            .Where(t => t.WorkspaceId == workspaceId && t.Id == testId)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.AttemptCount, t => t.AttemptCount + 1), cancellationToken);

    /// <summary>Records the lead on an attempt that has none yet; false when another request linked it first.</summary>
    public async Task<bool> LinkLeadAsync(Guid workspaceId, Guid attemptId, Guid leadId, DateTime? sharedAtUtc, CancellationToken cancellationToken) =>
        await db.Set<TestAttempt>().IgnoreQueryFilters()
            .Where(a => a.WorkspaceId == workspaceId && a.Id == attemptId && a.LeadId == null)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.LeadId, leadId).SetProperty(a => a.ResultSharedAtUtc, sharedAtUtc), cancellationToken) == 1;

    public async Task<IReadOnlyList<(Guid WorkspaceId, string Slug, string Title, DateTime PublishedAtUtc)>> ListPublishedAsync(
        IReadOnlyCollection<Guid> workspaceIds, CancellationToken cancellationToken)
    {
        if (workspaceIds.Count == 0)
        {
            return [];
        }

        var rows = await db.Set<DiagnosticTest>().IgnoreQueryFilters().AsNoTracking()
            .Where(t => workspaceIds.Contains(t.WorkspaceId) && t.Status == TestStatus.Published && t.ArchivedAtUtc == null)
            .OrderBy(t => t.Slug)
            .Select(t => new { t.WorkspaceId, t.Slug, t.Title, PublishedAtUtc = t.PublishedAtUtc!.Value })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => (r.WorkspaceId, r.Slug, r.Title, r.PublishedAtUtc))];
    }
}
