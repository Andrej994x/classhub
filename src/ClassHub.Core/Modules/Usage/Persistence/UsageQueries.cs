using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Usage.Contracts;
using ClassHub.Core.Modules.Usage.Domain;
using ClassHub.Core.Modules.Workspaces.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Modules.Usage.Persistence;

/// <summary>
/// The usage counters' writes and the platform-wide reads (allow-listed tenant bypass: totals across workspaces for staff,
/// never a workspace's own rows).
/// </summary>
internal sealed class UsageQueries(ClassHubDbContext db)
{
    /// <summary>One more use on the day, atomically (parallel uses never lose a count or collide on the key).</summary>
    public Task IncrementAsync(Guid workspaceId, DateOnly day, UsageEvent usage, string subject, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"""
            MERGE [UsageCounters] WITH (HOLDLOCK) AS t
            USING (SELECT {workspaceId} AS [WorkspaceId], {day} AS [Day], {(byte)usage} AS [Event], {subject} AS [Subject]) AS s
            ON t.[WorkspaceId] = s.[WorkspaceId] AND t.[Day] = s.[Day] AND t.[Event] = s.[Event] AND t.[Subject] = s.[Subject]
            WHEN MATCHED THEN UPDATE SET t.[Count] = t.[Count] + 1
            WHEN NOT MATCHED THEN INSERT ([WorkspaceId], [Day], [Event], [Subject], [Count]) VALUES (s.[WorkspaceId], s.[Day], s.[Event], s.[Subject], 1);
            """,
            cancellationToken);

    private IQueryable<UsageCounter> AllWorkspaces(DateOnly from, DateOnly to) =>
        db.Set<UsageCounter>().IgnoreQueryFilters().AsNoTracking().Where(c => c.Day >= from && c.Day <= to);

    public async Task<List<UsageTotal>> PlatformTotalsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await AllWorkspaces(from, to).GroupBy(c => new { c.Event, c.Subject })
            .Select(g => new UsageTotal(g.Key.Event, g.Key.Subject, g.Sum(c => c.Count))).ToListAsync(cancellationToken);

    public async Task<List<UsageDailyTotal>> PlatformDailyAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await AllWorkspaces(from, to).GroupBy(c => new { c.Day, c.Event })
            .Select(g => new UsageDailyTotal(g.Key.Day, g.Key.Event, g.Sum(c => c.Count))).ToListAsync(cancellationToken);

    public Task<int> WorkspacesUsingAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        AllWorkspaces(from, to).Select(c => c.WorkspaceId).Distinct().CountAsync(cancellationToken);
}

internal sealed class UsageCounterConfiguration : IEntityTypeConfiguration<UsageCounter>
{
    public void Configure(EntityTypeBuilder<UsageCounter> builder)
    {
        builder.ToTable("UsageCounters", t =>
        {
            t.HasCheckConstraint("CK_UsageCounters_Event", "[Event] BETWEEN 1 AND 3");
            t.HasCheckConstraint("CK_UsageCounters_Count", "[Count] >= 0");
        });
        builder.HasKey(x => new { x.WorkspaceId, x.Day, x.Event, x.Subject });
        builder.Property(x => x.Event).HasConversion<byte>();
        builder.Property(x => x.Subject).HasMaxLength(UsageCounter.SubjectMaxLength).IsUnicode(false);
        builder.HasIndex(x => new { x.Day, x.Event });
        builder.HasOne(WorkspaceForeignKeyTargets.Workspace, navigationName: null).WithMany().HasForeignKey(nameof(UsageCounter.WorkspaceId)).OnDelete(DeleteBehavior.NoAction);
    }
}
