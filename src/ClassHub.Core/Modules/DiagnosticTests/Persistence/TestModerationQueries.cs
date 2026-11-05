using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.DiagnosticTests.Contracts;
using ClassHub.Core.Modules.DiagnosticTests.Domain;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.DiagnosticTests.Persistence;

/// <summary>
/// Moderation reads and changes of tests across tenants (allow-listed tenant-filter bypass). Every query filters by
/// workspace; archived tests are included for the admin views (the archive filter is bypassed too).
/// </summary>
internal sealed class TestModerationQueries(ClassHubDbContext db) : ITestModeration
{
    private IQueryable<DiagnosticTest> Tests() => db.Set<DiagnosticTest>().IgnoreQueryFilters();

    /// <summary>Serializes moderation changes of one test until the caller's transaction ends.</summary>
    private async Task<DiagnosticTest?> LockedTestAsync(Guid workspaceId, Guid testId, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Moderation changes run inside the caller's transaction.");
        }

        var resource = $"classhub:test-moderation:{testId:N}";
        await db.Database.ExecuteSqlAsync(
            $"DECLARE @result int; EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; IF @result < 0 THROW 50006, 'Could not acquire the test moderation lock.', 1;",
            cancellationToken);
        return await Tests().SingleOrDefaultAsync(t => t.WorkspaceId == workspaceId && t.Id == testId, cancellationToken);
    }

    public async Task<ModeratedTest?> FindPublicAsync(Guid workspaceId, string testSlug, CancellationToken cancellationToken) =>
        await Tests().AsNoTracking()
            .Where(t => t.WorkspaceId == workspaceId && t.Slug == testSlug && t.Status == TestStatus.Published && t.ArchivedAtUtc == null && t.HiddenAtUtc == null)
            .Select(t => new ModeratedTest(t.WorkspaceId, t.Id, t.Slug, t.Title, true, null, null))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<ModeratedTest?> FindAsync(Guid workspaceId, Guid testId, CancellationToken cancellationToken) =>
        await Tests().AsNoTracking()
            .Where(t => t.WorkspaceId == workspaceId && t.Id == testId)
            .Select(t => new ModeratedTest(t.WorkspaceId, t.Id, t.Slug, t.Title,
                t.Status == TestStatus.Published && t.ArchivedAtUtc == null && t.HiddenAtUtc == null, t.HiddenReason, t.HiddenAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> HideAsync(Guid workspaceId, Guid testId, string reason, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var test = await LockedTestAsync(workspaceId, testId, cancellationToken);
        if (test is null || test.HiddenAtUtc is not null)
        {
            return false;
        }

        // The version is not touched: the teacher's open editor keeps working, and its saves never clear the flag.
        test.HiddenAtUtc = nowUtc;
        test.HiddenReason = reason;
        return true;
    }

    public async Task<bool> UnhideAsync(Guid workspaceId, Guid testId, CancellationToken cancellationToken)
    {
        var test = await LockedTestAsync(workspaceId, testId, cancellationToken);
        if (test?.HiddenAtUtc is null)
        {
            return false;
        }

        test.HiddenAtUtc = null;
        test.HiddenReason = null;
        return true;
    }

    public async Task<IReadOnlyList<ModeratedTest>> ListHiddenAsync(CancellationToken cancellationToken) =>
        await Tests().AsNoTracking()
            .Where(t => t.HiddenAtUtc != null)
            .OrderByDescending(t => t.HiddenAtUtc)
            .Select(t => new ModeratedTest(t.WorkspaceId, t.Id, t.Slug, t.Title, false, t.HiddenReason, t.HiddenAtUtc))
            .ToListAsync(cancellationToken);
}
