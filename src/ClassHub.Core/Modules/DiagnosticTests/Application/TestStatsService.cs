using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.DiagnosticTests.Contracts;
using ClassHub.Core.Modules.DiagnosticTests.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.DiagnosticTests.Application;

/// <summary>Dashboard numbers (tenant-filtered): published tests, attempts and shared results since a date.</summary>
internal sealed class TestStatsService(ClassHubDbContext db) : ITestStats
{
    public async Task<TestStats> GetAsync(DateTime fromUtc, CancellationToken cancellationToken)
    {
        var published = await db.Set<DiagnosticTest>().CountAsync(t => t.Status == TestStatus.Published, cancellationToken);
        var attempts = db.Set<TestAttempt>().Where(a => a.CompletedAtUtc >= fromUtc);
        return new TestStats(
            published,
            await attempts.CountAsync(cancellationToken),
            await attempts.CountAsync(a => a.ResultSharedAtUtc != null, cancellationToken));
    }
}
